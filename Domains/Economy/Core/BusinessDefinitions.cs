using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace LandLedgers.Economy
{
    public enum BusinessType
    {
        GeneralStore = 0,
        Blacksmith = 1,
        Butcher = 2,
        Ranch = 3,
        CropFarm = 4,
        Doctor = 5,
        Sawmill = 6,
        LumberYard = 7,
        BoardingHouse = 8,
        LiveryFreight = 9,
        Builder = 10,
        FuelDealer = 11,
        GrainMill = 12,
        Bakery = 13,
        Tailor = 14,
        Saloon = 15,
        Barber = 16,
        Wheelwright = 17,
        Mine = 18,
        /// <summary>
        /// EQP-5: the tannery as a distinct trade (historical: the tanner stood
        /// between the butcher and the leatherworker). Turns hides + bark tannin
        /// into workable leather — closes the saddler/harness supply hole.
        /// </summary>
        Tannery = 19,
        /// <summary>
        /// NX-2A: the post office. Canon Part VII §7.1-7.2: postal service is a
        /// real information network. Canon §16.4: the post office can be a paying
        /// transportation customer (star-route contracts). Historically a federal
        /// appointment; in-game it is created through the BIZ-1 workflow like any
        /// business, with the postmaster appointment as its authority.
        /// </summary>
        PostOffice = 20,
        /// <summary>
        /// NX-3A: the newspaper. Canon §5.8 / GHOST-DES-076: advertising is a
        /// real media market — publication location, circulation, frequency,
        /// placement inventory, rates, repeat terms. Closes T2B's upstream
        /// hole: recruitment ads buy through a real paper or are refused.
        /// </summary>
        Newspaper = 21
    }

    public enum BusinessCadence
    {
        Daily = 0,
        Weekly = 1,
        Monthly = 2
    }

    [Serializable]
    public sealed class WorkerSlotDefinition
    {
        [SerializeField]
        private string slotId = "worker";

        [SerializeField]
        private string displayName = "Worker";

        [SerializeField, Min(0)]
        private int baselineWeeklyWageCents = 1200;

        [SerializeField]
        [FormerlySerializedAs("requiredForMvp")]
        private bool requiredForOpening;

        public string SlotId => string.IsNullOrWhiteSpace(slotId) ? displayName : slotId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? SlotId : displayName;
        public int BaselineWeeklyWageCents => Mathf.Max(0, baselineWeeklyWageCents);
        public bool RequiredForOpening => requiredForOpening;
    }

    [Serializable]
    public sealed class BusinessOperationsDefinition
    {
        [SerializeField]
        private BusinessCadence salesCadence = BusinessCadence.Daily;

        [SerializeField]
        private BusinessCadence payrollCadence = BusinessCadence.Weekly;

        [SerializeField]
        private BusinessCadence reorderCadence = BusinessCadence.Weekly;

        [SerializeField]
        private BusinessCadence stockReviewCadence = BusinessCadence.Weekly;

        [SerializeField]
        private BusinessCadence marketSummaryCadence = BusinessCadence.Monthly;

        public BusinessCadence SalesCadence => salesCadence;
        public BusinessCadence PayrollCadence => payrollCadence;
        public BusinessCadence ReorderCadence => reorderCadence;
        public BusinessCadence StockReviewCadence => stockReviewCadence;
        public BusinessCadence MarketSummaryCadence => marketSummaryCadence;
    }

    [Serializable]
    public sealed class BusinessEconomyDefinition
    {
        [SerializeField, Min(0)]
        private int startingCashCents = 25000;

        [SerializeField, Min(0)]
        private int weeklyReorderReserveCents = 8000;

        [SerializeField, Min(0f)]
        private float defaultRetailMarkupMultiplier = 1.15f;

        [SerializeField, Range(0f, 1f)]
        private float lowStockWarningThreshold01 = 0.25f;

        [SerializeField, Range(0f, 2f)]
        private float reliabilitySensitivity = 0.5f;

        [SerializeField, Range(0f, 2f)]
        private float competitionSensitivity = 0.25f;

        public int StartingCashCents => Mathf.Max(0, startingCashCents);
        public int WeeklyReorderReserveCents => Mathf.Max(0, weeklyReorderReserveCents);
        public float DefaultRetailMarkupMultiplier => defaultRetailMarkupMultiplier <= 0f ? 1f : defaultRetailMarkupMultiplier;
        public float LowStockWarningThreshold01 => Mathf.Clamp01(lowStockWarningThreshold01);
        public float ReliabilitySensitivity => Mathf.Max(0f, reliabilitySensitivity);
        public float CompetitionSensitivity => Mathf.Max(0f, competitionSensitivity);
    }

    [Serializable]
    public sealed class BusinessDefinition
    {
        [SerializeField]
        private string businessId = "business";

        [SerializeField]
        private string displayName = "Business";

        [SerializeField]
        private BusinessType businessType;

        [SerializeField]
        [Tooltip("Legacy serialized field. Business instances now own assigned building identity.")]
        private string buildingId = string.Empty;

        [SerializeField]
        private BusinessOperationsDefinition operations = new();

        [SerializeField]
        private BusinessEconomyDefinition economy = new();

        [SerializeField]
        private string[] ownedCategoryIds = Array.Empty<string>();

        [SerializeField]
        private WorkerSlotDefinition[] workerSlots = Array.Empty<WorkerSlotDefinition>();

        public string BusinessId => string.IsNullOrWhiteSpace(businessId) ? displayName : businessId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? BusinessId : displayName;
        public BusinessType BusinessType => businessType;

        /// <summary>Default construction for Unity serialization.</summary>
        public BusinessDefinition()
        {
        }

        /// <summary>
        /// BIZ-1: code-constructed definition for fallback profiles (business types
        /// without an authored profile). Serialized fields keep their defaults otherwise.
        /// </summary>
        public BusinessDefinition(BusinessType businessType, string businessId, string displayName)
        {
            this.businessType = businessType;
            this.businessId = string.IsNullOrWhiteSpace(businessId) ? businessType.ToString() : businessId;
            this.displayName = string.IsNullOrWhiteSpace(displayName) ? businessType.ToString() : displayName;
        }
        public string BuildingId => buildingId ?? string.Empty;
        public BusinessOperationsDefinition Operations => operations ?? new BusinessOperationsDefinition();
        public BusinessEconomyDefinition Economy => economy ?? new BusinessEconomyDefinition();
        public ReadOnlySpan<string> OwnedCategoryIds => ownedCategoryIds ?? Array.Empty<string>();
        public ReadOnlySpan<WorkerSlotDefinition> WorkerSlots => workerSlots ?? Array.Empty<WorkerSlotDefinition>();

        public bool OwnsCategory(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId) || ownedCategoryIds == null)
            {
                return false;
            }

            for (int i = 0; i < ownedCategoryIds.Length; i++)
            {
                if (string.Equals(ownedCategoryIds[i], categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
