using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy;
using LandLedgers.MVP;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Delivery
{
    /// <summary>
    /// Sale settlement at delivery. Returns true when the sale settled;
    /// diagnostics explain refusal. Lots are aged BEFORE the executor runs,
    /// so only arrival-fresh goods can settle.
    /// </summary>
    public delegate bool DeliverySaleExecutor(
        DeliveryJob job, List<ITransitLot> lots, int dayIndex, List<string> diagnostics);

    /// <summary>
    /// FVS-3: delivery orchestration for farm sales. The vertical slice's core
    /// rule: a sale is AGREED up front but SETTLES ON DELIVERY. Ownership and
    /// risk transfer when the buyer takes physical custody — the conservative,
    /// canon-grounded choice (Canon §9.4 receiving arrangements; MR-P001:
    /// physical movement is what counts, never booked intent).
    ///
    /// If perishables spoil in transit, the sale fails and the loss sits with
    /// the seller. Nothing about a planned delivery books revenue early.
    /// </summary>
    public static class DeliveryService
    {
        /// <summary>Calibration: wagon speed for drive-time estimates.</summary>
        public const float WagonMilesPerHour = 3f;
        /// <summary>Calibration: freight charge base + per unit-mile.</summary>
        public const int FreightBaseCents = 50;
        public const int FreightCentsPerUnitMile = 2;

        /// <summary>
        /// Plans a delivery: validates the hauler option and records the job.
        /// No revenue is booked; lots are merely reserved by the agreement.
        /// </summary>
        public static DeliveryJob PlanDelivery(
            string saleAgreementId,
            string productKind,
            List<ITransitLot> lots,
            string originFarmId,
            string originBusinessId,
            string destinationBusinessId,
            string destinationName,
            DeliveryHaulerOption hauler,
            float distanceMilesOneWay,
            int dayIndex,
            string freightPayerBusinessId,
            string haulerWorkerName,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();

            if (lots == null || lots.Count == 0)
            {
                diagnostics.Add("DeliveryService: no lots to deliver.");
                return null;
            }
            if (hauler == DeliveryHaulerOption.Unspecified)
            {
                diagnostics.Add("DeliveryService: hauler option not specified — pick who actually moves the goods.");
                return null;
            }
            if (hauler == DeliveryHaulerOption.FreightCompany && string.IsNullOrWhiteSpace(freightPayerBusinessId))
            {
                diagnostics.Add("DeliveryService: freight-company hauling needs a named freight payer (Canon §3.6).");
                return null;
            }
            if ((hauler == DeliveryHaulerOption.FarmEmployee || hauler == DeliveryHaulerOption.BuyerCollects)
                && string.IsNullOrWhiteSpace(haulerWorkerName))
            {
                diagnostics.Add("DeliveryService: employee hauling must name the worker (labor provenance).");
                return null;
            }

            int units = 0;
            var lotIds = new List<string>();
            foreach (var lot in lots)
            {
                if (lot == null) continue;
                units += lot.TransitQuantityUnits;
                lotIds.Add(lot.TransitLotId);
            }

            if (units <= 0)
            {
                diagnostics.Add("DeliveryService: lots carry no units — nothing to haul (MR-P001).");
                return null;
            }

            var job = new DeliveryJob
            {
                JobId = "delivery-" + (saleAgreementId ?? "open") + "-" + dayIndex,
                SaleAgreementId = saleAgreementId ?? string.Empty,
                ProductKind = productKind ?? string.Empty,
                LotIds = lotIds,
                QuantityUnits = units,
                OriginFarmId = originFarmId ?? string.Empty,
                OriginBusinessId = originBusinessId ?? string.Empty,
                DestinationBusinessId = destinationBusinessId ?? string.Empty,
                DestinationName = destinationName ?? string.Empty,
                Hauler = hauler,
                FreightPayerBusinessId = freightPayerBusinessId ?? string.Empty,
                HaulerWorkerName = haulerWorkerName ?? string.Empty,
                DistanceMilesOneWay = distanceMilesOneWay,
                Status = DeliveryJobStatus.Planned,
                PlannedDayIndex = dayIndex,
            };

            if (hauler == DeliveryHaulerOption.FreightCompany)
            {
                job.FreightChargeCents = ComputeFreightChargeCents(units, distanceMilesOneWay);
            }
            else if (hauler == DeliveryHaulerOption.FarmOwnTeam || hauler == DeliveryHaulerOption.FarmEmployee)
            {
                int loadUnload = FreightCompanyRuntime.FreightMinutes(units, FreightGoodsClass.Perishable) * 2;
                int drive = EstimateDriveMinutesOneWay(distanceMilesOneWay);
                job.InternalCost = InternalHaulCost.Estimate(loadUnload, drive, 2, Math.Max(0f, distanceMilesOneWay));
            }

            return job;
        }

        /// <summary>
        /// JRN-2: journey-aware delivery planning. Resolves real miles through
        /// the journey model (farmstead node → business node) and plans the
        /// delivery on those miles — so drive times, internal haul costs, and
        /// freight charges derive from the 1:1 world scale (Canon §13.1)
        /// instead of abstract legs. Falls back to the legacy distance honestly
        /// (with a diagnostic) when the model cannot route.
        /// </summary>
        public static DeliveryJob PlanDeliveryWithJourney(
            string saleAgreementId,
            string productKind,
            List<ITransitLot> lots,
            string originFarmId,
            string originBusinessId,
            string destinationBusinessId,
            string destinationName,
            DeliveryHaulerOption hauler,
            float distanceMilesOneWay,
            int dayIndex,
            string freightPayerBusinessId,
            string haulerWorkerName,
            JourneyModel journeyModel,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            float miles = Math.Max(0f, distanceMilesOneWay);

            if (journeyModel != null)
            {
                string originLoc = JourneyTravel.LocationIdForFarm(originFarmId);
                JourneyLocation originBusinessLoc =
                    JourneyTravel.FindBusinessLocation(journeyModel, originBusinessId);
                if (originBusinessLoc != null) originLoc = originBusinessLoc.LocationId;

                JourneyLocation destLoc =
                    JourneyTravel.FindBusinessLocation(journeyModel, destinationBusinessId);
                if (destLoc != null)
                {
                    JourneyTravelEstimate estimate =
                        JourneyTravel.EstimateDrive(journeyModel, originLoc, destLoc.LocationId);
                    if (estimate.FromModel)
                    {
                        miles = estimate.Miles;
                        diagnostics.Add($"DeliveryService: journey model routed {originLoc} → {destLoc.LocationId}: " +
                            $"{estimate.Miles:F1} mi ≈ {estimate.MinutesOneWay} min by wagon.");
                    }
                    else
                    {
                        diagnostics.Add("DeliveryService: journey model could not route — legacy distance kept: "
                            + estimate.Diagnostic);
                    }
                }
                else
                {
                    diagnostics.Add($"DeliveryService: destination business '{destinationBusinessId}' has no journey location — legacy distance kept.");
                }
            }

            return PlanDelivery(
                saleAgreementId, productKind, lots,
                originFarmId, originBusinessId,
                destinationBusinessId, destinationName,
                hauler, miles, dayIndex,
                freightPayerBusinessId, haulerWorkerName,
                diagnostics);
        }

        public static string DepartDelivery(DeliveryJob job, int dayIndex)
        {
            if (job == null) return "DeliveryService: no job.";
            if (job.Status != DeliveryJobStatus.Planned)
            {
                return $"DeliveryService: job {job.JobId} is {job.Status}; only planned jobs depart.";
            }
            job.Status = DeliveryJobStatus.InTransit;
            job.DepartedDayIndex = dayIndex;
            return null;
        }

        /// <summary>
        /// Completes a delivery: ages the lots by the transit days, then runs
        /// the sale executor. Settlement happens here or not at all.
        /// </summary>
        public static string CompleteDelivery(
            DeliveryJob job,
            List<ITransitLot> lots,
            int dayIndex,
            int transitDays,
            DeliverySaleExecutor saleExecutor,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (job == null) return "DeliveryService: no job.";
            if (job.Status != DeliveryJobStatus.InTransit)
            {
                return $"DeliveryService: job {job.JobId} is {job.Status}; only in-transit jobs complete.";
            }

            // Perishables age in transit — transport time affects saleable state (Tech X §8.4).
            if (lots != null)
            {
                foreach (var lot in lots)
                {
                    if (lot != null) lot.AgeInTransit(Math.Max(0, transitDays));
                }
            }

            bool settled = false;
            if (saleExecutor != null)
            {
                settled = saleExecutor(job, lots ?? new List<ITransitLot>(), dayIndex, diagnostics);
            }
            else
            {
                diagnostics.Add($"DeliveryService: job {job.JobId} arrived but no sale executor — goods are with the buyer, settlement is unrecorded. This is a wiring gap, not revenue.");
            }

            job.DeliveredDayIndex = dayIndex;
            if (settled)
            {
                job.Status = DeliveryJobStatus.Delivered;
                diagnostics.Add($"DeliveryService: job {job.JobId} delivered and settled on day {dayIndex}.");
            }
            else
            {
                job.Status = DeliveryJobStatus.Failed;
                job.FailureReason = "Sale did not settle on delivery (see diagnostics); seller bears the loss.";
                diagnostics.Add($"DeliveryService: job {job.JobId} FAILED to settle — " + job.FailureReason);
            }
            return null;
        }

        /// <summary>
        /// Builds the BIZ-3 shipment for the FreightCompany hauler option. The
        /// caller runs it through TryAcceptShipment → TryDispatchShipment →
        /// (on completion) CollectFreightCharge on the freight company runtime:
        /// one physical pool, external payer only (Canon §3.6, §11.2).
        /// </summary>
        public static LogisticsShipmentState BuildFreightShipment(DeliveryJob job, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (job == null)
            {
                diagnostics.Add("DeliveryService: no job to ship.");
                return null;
            }
            if (job.Hauler != DeliveryHaulerOption.FreightCompany)
            {
                diagnostics.Add($"DeliveryService: job {job.JobId} hauler is {job.Hauler}, not the freight company.");
                return null;
            }

            var shipment = new LogisticsShipmentState
            {
                shipmentId = "fr-" + job.JobId,
                cargoClass = job.IsPerishable ? LogisticsCargoClass.Perishables : LogisticsCargoClass.PackagedGoods,
                plannedQuantityUnits = job.QuantityUnits,
                remainingQuantityUnits = job.QuantityUnits,
                freightChargeCents = job.FreightChargeCents,
                freightPayerBusinessInstanceId = job.FreightPayerBusinessId,
                sourceBusinessInstanceId = job.OriginBusinessId,
                destinationBusinessInstanceId = job.DestinationBusinessId,
                summaryLabel = $"{job.QuantityUnits} {job.ProductKind} ({job.OriginFarmId} → {job.DestinationName})",
            };
            return shipment;
        }

        /// <summary>
        /// Posts an internal haul's cost to the farm household ledger as an
        /// outflow with provenance (Canon XIII §13.2). Internal hauling is a
        /// cost, never revenue (Canon §11.2).
        /// </summary>
        public static string PostInternalHaulCost(
            HouseholdLedger farmLedger, DeliveryJob job, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (farmLedger == null) return "DeliveryService: no farm ledger for haul cost.";
            if (job == null) return "DeliveryService: no job.";
            if (job.InternalCost == null) return $"DeliveryService: job {job.JobId} has no internal cost recorded.";

            int cents = job.InternalCost.TotalCostCents;
            if (cents <= 0) return null;

            farmLedger.RecordOutflow(dayIndex, cents,
                $"Internal haulage: {job.QuantityUnits} {job.ProductKind} to {job.DestinationName} " +
                $"(feed {job.InternalCost.FeedCostCents}c + wear {job.InternalCost.WearCostCents}c; " +
                $"{job.InternalCost.LaborMinutes} labor min) — internal cost, not revenue (Canon §11.2).",
                job.DestinationName);
            return null;
        }

        /// <summary>
        /// Enqueues loading/unloading tasks for a farm-run delivery through
        /// TaskAuthority (TTS-5 freight math). The farm reuses the shared
        /// freight task definitions — loading is loading.
        /// </summary>
        public static void EnqueueHandlingTasks(
            TaskAuthority authority, DeliveryJob job, EntityId ownerId, int dayIndex)
        {
            if (authority == null || job == null) return;
            if (job.Hauler != DeliveryHaulerOption.FarmOwnTeam && job.Hauler != DeliveryHaulerOption.FarmEmployee)
            {
                return;
            }
            int minutes = FreightCompanyRuntime.FreightMinutes(job.QuantityUnits, FreightGoodsClass.Perishable);
            authority.CreateTaskWithPlannedMinutes(
                FreightCompanyRuntime.LoadFreightTaskId, ownerId, dayIndex, minutes, job.JobId);
            authority.CreateTaskWithPlannedMinutes(
                FreightCompanyRuntime.UnloadFreightTaskId, ownerId, dayIndex, minutes, job.JobId);
        }

        public static int ComputeFreightChargeCents(int quantityUnits, float distanceMilesOneWay)
        {
            float miles = Math.Max(0f, distanceMilesOneWay);
            return FreightBaseCents + Mathf.RoundToInt(quantityUnits * miles * FreightCentsPerUnitMile);
        }

        public static int EstimateDriveMinutesOneWay(float distanceMiles)
        {
            if (distanceMiles <= 0f) return 30; // unknown distance: conservative half hour
            return Math.Max(5, Mathf.RoundToInt(distanceMiles / WagonMilesPerHour * 60f));
        }
    }
}
