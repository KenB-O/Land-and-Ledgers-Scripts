using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Delivery
{
    /// <summary>
    /// FVS-3: who hauls a farm delivery. Kennedy's four options, each with
    /// honest cost/revenue treatment (Canon §11.2 portfolio freight).
    /// </summary>
    public enum DeliveryHaulerOption
    {
        Unspecified = 0,
        /// <summary>Farm's own wagon and team: internal cost (feed, wear, labor
        /// time). Never revenue to the farm (Canon §11.2).</summary>
        FarmOwnTeam = 1,
        /// <summary>A farm employee runs the delivery: still the farm's cost,
        /// with the worker named (labor provenance).</summary>
        FarmEmployee = 2,
        /// <summary>The buyer's employee collects: the buyer's cost; the farm
        /// tenders the goods at the farm gate.</summary>
        BuyerCollects = 3,
        /// <summary>The freight company hauls for a paid charge: external payer
        /// → real freight revenue through the BIZ-3 flow (Canon §3.6).</summary>
        FreightCompany = 4,
    }

    public enum DeliveryJobStatus
    {
        Unspecified = 0,
        Planned = 1,
        InTransit = 2,
        Delivered = 3,
        Failed = 4,
    }

    /// <summary>
    /// FVS-3: internal haul cost record (Canon §11.2). Internal service consumes
    /// labor time, feed, and wagon wear — recorded as a cost, never as revenue.
    /// Calibration constants are tuning, not canon.
    /// </summary>
    [Serializable]
    public sealed class InternalHaulCost
    {
        /// <summary>Calibration: feed cost per draft animal per delivery day.</summary>
        public const int FeedCentsPerAnimalPerDay = 15;
        /// <summary>Calibration: wagon wear per loaded mile.</summary>
        public const int WearCentsPerMile = 8;

        public int LaborMinutes;      // load + drive + unload (TTS-5 math + drive estimate)
        public int FeedCostCents;
        public int WearCostCents;
        public int TotalCostCents => FeedCostCents + WearCostCents;
        public string Notes = string.Empty;

        public static InternalHaulCost Estimate(int loadUnloadMinutes, int driveMinutes,
            int draftAnimals, float milesOneWay)
        {
            int days = Math.Max(1, (int)Math.Ceiling((loadUnloadMinutes + driveMinutes * 2) / 480.0));
            return new InternalHaulCost
            {
                LaborMinutes = loadUnloadMinutes + driveMinutes * 2,
                FeedCostCents = draftAnimals * FeedCentsPerAnimalPerDay * days,
                WearCostCents = Mathf.RoundToInt(milesOneWay * 2 * WearCentsPerMile),
                Notes = $"{draftAnimals} draft animals, {milesOneWay} miles each way (calibration rates).",
            };
        }
    }

    /// <summary>
    /// FVS-3: a delivery job linking an AGREED sale to its hauling. The sale is
    /// not settled at plan time — settlement runs at delivery (ownership/risk
    /// transfer on delivery; see DeliveryService). If perishables spoil in
    /// transit, the sale fails and the loss sits with the seller.
    /// </summary>
    [Serializable]
    public sealed class DeliveryJob
    {
        public string JobId = string.Empty;
        public string SaleAgreementId = string.Empty; // the agreed (not settled) sale
        public string ProductKind = string.Empty;     // e.g. "milk", "butter", "eggs"
        public List<string> LotIds = new List<string>();
        public int QuantityUnits;
        public string OriginFarmId = string.Empty;
        public string OriginBusinessId = string.Empty;   // farm business instance id
        public string DestinationBusinessId = string.Empty;
        public string DestinationName = string.Empty;
        public DeliveryHaulerOption Hauler;
        public string FreightPayerBusinessId = string.Empty; // for FreightCompany option
        public string HaulerWorkerName = string.Empty;       // FarmEmployee/BuyerCollects: who
        public float DistanceMilesOneWay = -1f;              // -1 = unknown
        public DeliveryJobStatus Status;
        public int PlannedDayIndex = -1;
        public int DepartedDayIndex = -1;
        public int DeliveredDayIndex = -1;
        public string FailureReason = string.Empty;
        public InternalHaulCost InternalCost; // set for FarmOwnTeam/FarmEmployee
        public int FreightChargeCents;        // set for FreightCompany

        public DeliveryJob() { }

        public bool IsPerishable
        {
            get
            {
                string kind = (ProductKind ?? string.Empty).ToLowerInvariant();
                return kind.Contains("milk") || kind.Contains("butter") || kind.Contains("egg")
                    || kind.Contains("cheese") || kind.Contains("meat") || kind.Contains("cream");
            }
        }
    }
}
