using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Persistence;
using LandLedgers.World;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum MineDevelopmentStage
    {
        Prospect = 0,
        SurfaceWorks = 1,
        Shaft = 2,
        Expanded = 3
    }

    [System.Serializable]
    public sealed class MineRuntimeState
    {
        [SerializeField]
        private MineralResourceKind mineralKind;

        [SerializeField, Range(0f, 1f)]
        private float depositConfidence01 = 0.5f;

        [SerializeField]
        private MineDevelopmentStage developmentStage;

        [SerializeField, Min(0)]
        private int lastWeeklyOutputUnits;

        [SerializeField]
        private string stockpileCategoryId = string.Empty;

        [SerializeField, Range(0f, 1f)]
        private float safetyRisk01 = 0.2f;

        [SerializeField, Range(0f, 1f)]
        private float remainingRichness01 = 0.8f;

        [SerializeField, Range(0f, 1f)]
        private float campPressure01;

        [SerializeField, TextArea(1, 3)]
        private string lastWeeklyOutputSummary = string.Empty;

        /// <summary>W8A: the mine plan — shafts as property improvements, levels/drives as tracked workings, named veins. Lazy-initialized so older saves load with an empty plan.</summary>
        [SerializeField]
        private MineShaftPlan shaftPlan;

        /// <summary>W8B: the ore stockpile — real lots with grade/assayer provenance. Lazy-initialized so older saves load with an empty stock.</summary>
        [SerializeField]
        private MineOreStock oreStock;

        /// <summary>W8C: the crew roster — miner employment by role/shift/wage. Lazy-initialized so older saves load with an empty roster.</summary>
        [SerializeField]
        private MineLaborRegister laborRegister;

        /// <summary>W8D: installed hoisting plants (real equipment with condition). Lazy-initialized for legacy saves.</summary>
        [SerializeField]
        private MineHoistRegister hoistRegister;

        /// <summary>W8D: timbering consumption records (lumber demand link). Lazy-initialized for legacy saves.</summary>
        [SerializeField]
        private MineTimberingLedger timberingLedger;

        /// <summary>W8D: ore shipment orders to declared smelters. Persisted; the shipment service operates on this list.</summary>
        [SerializeField]
        private List<MineOreShipmentOrder> shipmentOrders = new List<MineOreShipmentOrder>();

        public MineralResourceKind MineralKind => mineralKind;
        public float DepositConfidence01 => Mathf.Clamp01(depositConfidence01);
        public MineDevelopmentStage DevelopmentStage => developmentStage;
        public int LastWeeklyOutputUnits => Mathf.Max(0, lastWeeklyOutputUnits);
        public string StockpileCategoryId => stockpileCategoryId ?? string.Empty;
        public float SafetyRisk01 => Mathf.Clamp01(safetyRisk01);
        public float RemainingRichness01 => Mathf.Clamp01(remainingRichness01);
        public float CampPressure01 => Mathf.Clamp01(campPressure01);
        public string LastWeeklyOutputSummary => string.IsNullOrWhiteSpace(lastWeeklyOutputSummary) ? "No mine output resolved yet." : lastWeeklyOutputSummary;

        /// <summary>W8A: the mine plan, never null (lazy-initialized for legacy saves).</summary>
        public MineShaftPlan ShaftPlan
        {
            get
            {
                if (shaftPlan == null)
                    shaftPlan = new MineShaftPlan();
                return shaftPlan;
            }
        }

        /// <summary>W8B: the ore stockpile, never null (lazy-initialized for legacy saves).</summary>
        public MineOreStock OreStock
        {
            get
            {
                if (oreStock == null)
                    oreStock = new MineOreStock();
                return oreStock;
            }
        }

        /// <summary>W8C: the crew roster, never null (lazy-initialized for legacy saves).</summary>
        public MineLaborRegister LaborRegister
        {
            get
            {
                if (laborRegister == null)
                    laborRegister = new MineLaborRegister();
                return laborRegister;
            }
        }

        /// <summary>W8D: installed hoisting plants, never null (lazy-initialized for legacy saves).</summary>
        public MineHoistRegister HoistRegister
        {
            get
            {
                if (hoistRegister == null)
                    hoistRegister = new MineHoistRegister();
                return hoistRegister;
            }
        }

        /// <summary>W8D: timbering consumption records, never null (lazy-initialized for legacy saves).</summary>
        public MineTimberingLedger TimberingLedger
        {
            get
            {
                if (timberingLedger == null)
                    timberingLedger = new MineTimberingLedger();
                return timberingLedger;
            }
        }

        /// <summary>W8D: the authoritative ore shipment order list (the shipment service operates on this).</summary>
        public List<MineOreShipmentOrder> ShipmentOrders
        {
            get
            {
                if (shipmentOrders == null)
                    shipmentOrders = new List<MineOreShipmentOrder>();
                return shipmentOrders;
            }
        }

        /// <summary>W8D: builds the shipment service around this runtime's order list.</summary>
        public MineOreShipmentService CreateShipmentService()
        {
            return new MineOreShipmentService(ShipmentOrders);
        }

        public static MineRuntimeState CreateDefault(MineralResourceKind kind)
        {
            return new MineRuntimeState
            {
                mineralKind = kind,
                depositConfidence01 = kind is MineralResourceKind.Gold or MineralResourceKind.Silver ? 0.46f : 0.62f,
                developmentStage = kind == MineralResourceKind.Coal ? MineDevelopmentStage.SurfaceWorks : MineDevelopmentStage.Prospect,
                stockpileCategoryId = GetStockpileCategoryId(kind),
                safetyRisk01 = kind is MineralResourceKind.Coal or MineralResourceKind.Iron ? 0.24f : 0.34f,
                remainingRichness01 = kind is MineralResourceKind.Gold or MineralResourceKind.Silver ? 0.68f : 0.84f,
                campPressure01 = kind is MineralResourceKind.Gold or MineralResourceKind.Silver ? 0.18f : 0.10f,
                lastWeeklyOutputSummary = "No mine output resolved yet."
            };
        }

        public void ConfigureSeed(
            MineralResourceKind kind,
            float depositConfidence01,
            MineDevelopmentStage developmentStage,
            float safetyRisk01,
            float remainingRichness01,
            float campPressure01)
        {
            mineralKind = kind;
            this.depositConfidence01 = Mathf.Clamp01(depositConfidence01);
            this.developmentStage = developmentStage;
            stockpileCategoryId = GetStockpileCategoryId(kind);
            this.safetyRisk01 = Mathf.Clamp01(safetyRisk01);
            this.remainingRichness01 = Mathf.Clamp01(remainingRichness01);
            this.campPressure01 = Mathf.Clamp01(campPressure01);
        }

        public void RecordWeeklyOutput(int outputUnits, string summary, float resolvedSafetyRisk01, float resolvedRichness01, float resolvedCampPressure01)
        {
            lastWeeklyOutputUnits = Mathf.Max(0, outputUnits);
            lastWeeklyOutputSummary = string.IsNullOrWhiteSpace(summary) ? "No mine output resolved yet." : summary.Trim();
            safetyRisk01 = Mathf.Clamp01(resolvedSafetyRisk01);
            remainingRichness01 = Mathf.Clamp01(resolvedRichness01);
            campPressure01 = Mathf.Clamp01(resolvedCampPressure01);
        }

        public MineRuntimeSaveDto CaptureSaveDto()
        {
            var shipmentLedger = new MineOreShipmentLedgerSaveDto();
            foreach (MineOreShipmentOrder order in ShipmentOrders)
                shipmentLedger.orders.Add(order.CaptureSaveDto());

            return new MineRuntimeSaveDto
            {
                mineralKind = MineralKind,
                depositConfidence01 = DepositConfidence01,
                developmentStage = DevelopmentStage,
                lastWeeklyOutputUnits = LastWeeklyOutputUnits,
                stockpileCategoryId = StockpileCategoryId,
                safetyRisk01 = SafetyRisk01,
                remainingRichness01 = RemainingRichness01,
                campPressure01 = CampPressure01,
                lastWeeklyOutputSummary = LastWeeklyOutputSummary,
                shaftPlan = ShaftPlan.CaptureSaveDto(),
                oreStock = OreStock.CaptureSaveDto(),
                laborRegister = LaborRegister.CaptureSaveDto(),
                hoistRegister = HoistRegister.CaptureSaveDto(),
                timberingLedger = TimberingLedger.CaptureSaveDto(),
                shipmentLedger = shipmentLedger,
            };
        }

        public static MineRuntimeState FromSaveDto(MineRuntimeSaveDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            MineRuntimeState state = CreateDefault(dto.mineralKind);
            state.depositConfidence01 = Mathf.Clamp01(dto.depositConfidence01);
            state.developmentStage = dto.developmentStage;
            state.lastWeeklyOutputUnits = Mathf.Max(0, dto.lastWeeklyOutputUnits);
            state.stockpileCategoryId = string.IsNullOrWhiteSpace(dto.stockpileCategoryId)
                ? GetStockpileCategoryId(dto.mineralKind)
                : dto.stockpileCategoryId;
            state.safetyRisk01 = Mathf.Clamp01(dto.safetyRisk01);
            state.remainingRichness01 = Mathf.Clamp01(dto.remainingRichness01);
            state.campPressure01 = Mathf.Clamp01(dto.campPressure01);
            state.lastWeeklyOutputSummary = string.IsNullOrWhiteSpace(dto.lastWeeklyOutputSummary)
                ? "No mine output resolved yet."
                : dto.lastWeeklyOutputSummary;
            state.shaftPlan = MineShaftPlan.FromSaveDto(dto.shaftPlan);
            state.oreStock = MineOreStock.FromSaveDto(dto.oreStock);
            state.laborRegister = MineLaborRegister.FromSaveDto(dto.laborRegister);
            state.hoistRegister = MineHoistRegister.FromSaveDto(dto.hoistRegister);
            state.timberingLedger = MineTimberingLedger.FromSaveDto(dto.timberingLedger);
            state.shipmentOrders = new List<MineOreShipmentOrder>();
            if (dto.shipmentLedger != null)
            {
                foreach (MineOreShipmentOrderSaveDto orderDto in dto.shipmentLedger.orders)
                {
                    MineOreShipmentOrder order = MineOreShipmentOrder.FromSaveDto(orderDto);
                    if (order != null)
                        state.shipmentOrders.Add(order);
                }
            }
            return state;
        }

        public bool HasMaterialPressureSignal => CalculateMineSupportPressure01(null) >= 0.35f;

        public float CalculateMineSupportPressure01(BusinessInstanceState owningBusiness)
        {
            float efficiencyGap = owningBusiness != null ? 1f - Mathf.Clamp01(owningBusiness.OperatingEfficiency01) : 0f;
            float noOutputPressure = DevelopmentStage != MineDevelopmentStage.Prospect && LastWeeklyOutputUnits <= 0 ? 0.55f : 0f;
            float richnessPressure = 1f - RemainingRichness01;
            float confidencePressure = 1f - DepositConfidence01;
            float blockedPressure = LastWeeklyOutputSummary.Contains("blocked", System.StringComparison.OrdinalIgnoreCase)
                || LastWeeklyOutputSummary.Contains("short", System.StringComparison.OrdinalIgnoreCase)
                || LastWeeklyOutputSummary.Contains("delay", System.StringComparison.OrdinalIgnoreCase)
                    ? 0.65f
                    : 0f;

            return Mathf.Clamp01(Mathf.Max(
                SafetyRisk01,
                CampPressure01,
                noOutputPressure,
                efficiencyGap,
                blockedPressure,
                richnessPressure * 0.65f,
                confidencePressure * 0.45f));
        }

        public string BuildMineReadinessSummary(BusinessInstanceState owningBusiness = null)
        {
            float pressure = CalculateMineSupportPressure01(owningBusiness);
            string owner = owningBusiness != null ? owningBusiness.RuntimeDisplayName : GetMineralDisplayName(MineralKind);
            return $"{owner}: {GetDevelopmentStageDisplayName(DevelopmentStage)}, output {LastWeeklyOutputUnits} {StockpileCategoryId}, support pressure {Mathf.RoundToInt(pressure * 100f)}%.";
        }

        public string BuildMinePressureDetail(BusinessInstanceState owningBusiness = null)
        {
            float efficiency = owningBusiness != null ? Mathf.Clamp01(owningBusiness.OperatingEfficiency01) : 1f;
            return $"Safety risk {Mathf.RoundToInt(SafetyRisk01 * 100f)}%, camp pressure {Mathf.RoundToInt(CampPressure01 * 100f)}%, confidence {Mathf.RoundToInt(DepositConfidence01 * 100f)}%, richness {Mathf.RoundToInt(RemainingRichness01 * 100f)}%, operating efficiency {Mathf.RoundToInt(efficiency * 100f)}%. Last output: {LastWeeklyOutputSummary}";
        }

        public string BuildMinePressureAction(BusinessInstanceState owningBusiness = null)
        {
            if (SafetyRisk01 >= 0.55f)
            {
                return "Improve mine safety, tools, supports, or supervision before pushing output harder.";
            }

            if (CampPressure01 >= 0.45f)
            {
                return "Add boarding, freight, food, or local services before camp strain becomes a wider settlement problem.";
            }

            if (owningBusiness != null && owningBusiness.OperatingEfficiency01 < 0.65f)
            {
                return "Staff or stabilize the mine before treating its output as reliable supply.";
            }

            return "Review mine supplies, hauling route, staffing, and support stock before expanding production.";
        }

        public static string GetStockpileCategoryId(MineralResourceKind kind)
        {
            return kind switch
            {
                MineralResourceKind.Coal => "coal",
                MineralResourceKind.Iron => "iron_ore",
                MineralResourceKind.Gold => "gold_ore",
                MineralResourceKind.Silver => "silver_ore",
                _ => "coal"
            };
        }

        public static string GetMineralDisplayName(MineralResourceKind kind)
        {
            return kind switch
            {
                MineralResourceKind.Iron => "Iron",
                MineralResourceKind.Gold => "Gold",
                MineralResourceKind.Silver => "Silver",
                _ => "Coal"
            };
        }

        public static string GetDevelopmentStageDisplayName(MineDevelopmentStage stage)
        {
            return stage switch
            {
                MineDevelopmentStage.SurfaceWorks => "Surface Works",
                MineDevelopmentStage.Shaft => "Shaft",
                MineDevelopmentStage.Expanded => "Expanded",
                _ => "Prospect"
            };
        }
    }
}
