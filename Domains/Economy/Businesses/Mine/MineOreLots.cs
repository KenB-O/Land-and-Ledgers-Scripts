using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// W8B: one mined ore lot with full provenance — which shaft, which
    /// level/drive, which named vein, which day it was broken, and how its
    /// grade was established. Grade is ESTIMATED (low confidence) until a
    /// skilled assayer runs it through the assay task; the estimate and the
    /// assay are both recorded, never silently replaced.
    /// </summary>
    [Serializable]
    public sealed class MineOreLot
    {
        [SerializeField]
        private EntityId lotId = EntityId.Invalid;

        [SerializeField]
        private MineralResourceKind mineralKind;

        [SerializeField, Min(0)]
        private int tons;

        /// <summary>Grade: oz/ton for gold/silver, % Fe for iron, BTU/lb for coal.</summary>
        [SerializeField, Min(0)]
        private float gradeValue;

        [SerializeField]
        private string gradeUnit = string.Empty;

        /// <summary>True once a skilled assayer has assayed this lot (W8B assay task).</summary>
        [SerializeField]
        private bool assayed;

        [SerializeField]
        private string assayId = string.Empty;

        [SerializeField]
        private EntityId assayerPersonId = EntityId.Invalid;

        [SerializeField, Min(-1)]
        private int assayDayIndex = -1;

        /// <summary>Confidence in the grade: 1.0 once assayed, lower for estimates.</summary>
        [SerializeField, Range(0f, 1f)]
        private float gradeConfidence01 = 0.3f;

        // --- origin provenance (never synthetic) ---
        [SerializeField]
        private string shaftId = string.Empty;

        [SerializeField]
        private string levelId = string.Empty;

        [SerializeField]
        private string veinId = string.Empty;

        [SerializeField, Min(0)]
        private int minedDayIndex;

        [SerializeField]
        private string stockpileCategoryId = string.Empty;

        public EntityId LotId => lotId;
        public MineralResourceKind MineralKind => mineralKind;
        public int Tons => Math.Max(0, tons);
        public float GradeValue => Math.Max(0f, gradeValue);
        public string GradeUnit => string.IsNullOrWhiteSpace(gradeUnit) ? DefaultGradeUnit(mineralKind) : gradeUnit;
        public bool Assayed => assayed;
        public string AssayId => assayId ?? string.Empty;
        public EntityId AssayerPersonId => assayerPersonId;
        public int AssayDayIndex => assayDayIndex;
        public float GradeConfidence01 => Mathf.Clamp01(gradeConfidence01);
        public string ShaftId => shaftId ?? string.Empty;
        public string LevelId => levelId ?? string.Empty;
        public string VeinId => veinId ?? string.Empty;
        public int MinedDayIndex => Math.Max(0, minedDayIndex);
        public string StockpileCategoryId => stockpileCategoryId ?? string.Empty;

        public MineOreLot() { }

        public static string DefaultGradeUnit(MineralResourceKind kind)
        {
            return kind switch
            {
                MineralResourceKind.Gold => "oz/ton",
                MineralResourceKind.Silver => "oz/ton",
                MineralResourceKind.Iron => "pct-fe",
                _ => "btu-per-lb",
            };
        }

        /// <summary>
        /// Creates a newly broken ore lot. Estimated grade at low confidence
        /// until assayed. Refuses tons &lt;= 0 loudly via the caller's
        /// diagnostics (constructor stays total).
        /// </summary>
        public static MineOreLot Create(EntityIdRegistry registry, MineralResourceKind mineralKind,
            int tons, float estimatedGradeValue, string shaftId, string levelId, string veinId,
            int minedDayIndex, string stockpileCategoryId)
        {
            var lot = new MineOreLot
            {
                lotId = registry != null ? registry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                mineralKind = mineralKind,
                tons = Math.Max(0, tons),
                gradeValue = Math.Max(0f, estimatedGradeValue),
                gradeUnit = DefaultGradeUnit(mineralKind),
                assayed = false,
                gradeConfidence01 = 0.3f,
                shaftId = shaftId ?? string.Empty,
                levelId = levelId ?? string.Empty,
                veinId = veinId ?? string.Empty,
                minedDayIndex = Math.Max(0, minedDayIndex),
                stockpileCategoryId = stockpileCategoryId ?? string.Empty,
            };
            return lot;
        }

        /// <summary>
        /// Records the assay result: the estimate is REPLACED by the measured
        /// grade and confidence goes to 1.0, but the lot keeps the assayer's
        /// identity, the assay id and the day as permanent provenance.
        /// </summary>
        public string RecordAssay(string assayId, EntityId assayerPersonId, int assayDayIndex, float assayedGradeValue)
        {
            if (string.IsNullOrWhiteSpace(assayId))
                return "MineOreLot.RecordAssay: assay id is required.";
            if (assayerPersonId.Equals(EntityId.Invalid))
                return "MineOreLot.RecordAssay: a real assayer person is required — no anonymous assays.";
            if (assayedGradeValue < 0f)
                return "MineOreLot.RecordAssay: grade cannot be negative.";

            this.assayId = assayId;
            this.assayerPersonId = assayerPersonId;
            this.assayDayIndex = Math.Max(0, assayDayIndex);
            gradeValue = assayedGradeValue;
            gradeConfidence01 = 1f;
            assayed = true;
            return null;
        }

        /// <summary>Removes tons from this lot (FIFO dispense). Returns the tons actually removed.</summary>
        public int RemoveTons(int requestedTons)
        {
            int removed = Math.Min(Tons, Math.Max(0, requestedTons));
            tons = Tons - removed;
            return removed;
        }

        public string ProvenanceChain(MineShaftPlan plan)
        {
            var parts = new List<string>();
            MineShaft shaft = plan?.FindShaft(shaftId);
            if (shaft != null) parts.Add($"shaft '{shaft.ShaftName}'");
            MineLevel level = plan?.FindLevel(levelId);
            if (level != null) parts.Add($"level {level.LevelNumber}");
            MineVein vein = !string.IsNullOrWhiteSpace(veinId) ? plan?.FindVein(veinId) : null;
            if (vein != null) parts.Add($"vein '{vein.VeinName}'");
            parts.Add($"mined day {MinedDayIndex}");
            parts.Add(Assayed ? $"assayed by P{AssayerPersonId.Id} day {AssayDayIndex}" : "unassayed estimate");
            return parts.Count == 0 ? "NO PROVENANCE" : string.Join(" | ", parts.ToArray());
        }

        public MineOreLotSaveDto CaptureSaveDto()
        {
            return new MineOreLotSaveDto
            {
                lotKind = (int)lotId.Kind,
                lotSeq = lotId.Id,
                mineralKind = mineralKind,
                tons = Tons,
                gradeValue = GradeValue,
                gradeUnit = GradeUnit,
                assayed = assayed,
                assayId = AssayId,
                assayerKind = (int)assayerPersonId.Kind,
                assayerSeq = assayerPersonId.Id,
                assayDayIndex = assayDayIndex,
                gradeConfidence01 = GradeConfidence01,
                shaftId = ShaftId,
                levelId = LevelId,
                veinId = VeinId,
                minedDayIndex = MinedDayIndex,
                stockpileCategoryId = StockpileCategoryId,
            };
        }

        public static MineOreLot FromSaveDto(MineOreLotSaveDto dto)
        {
            if (dto == null)
                return null;
            return new MineOreLot
            {
                lotId = new EntityId { Kind = (EntityKind)dto.lotKind, Id = dto.lotSeq },
                mineralKind = dto.mineralKind,
                tons = Math.Max(0, dto.tons),
                gradeValue = Math.Max(0f, dto.gradeValue),
                gradeUnit = dto.gradeUnit ?? DefaultGradeUnit(dto.mineralKind),
                assayed = dto.assayed,
                assayId = dto.assayId ?? string.Empty,
                assayerPersonId = new EntityId { Kind = (EntityKind)dto.assayerKind, Id = dto.assayerSeq },
                assayDayIndex = dto.assayDayIndex,
                gradeConfidence01 = Mathf.Clamp01(dto.gradeConfidence01),
                shaftId = dto.shaftId ?? string.Empty,
                levelId = dto.levelId ?? string.Empty,
                veinId = dto.veinId ?? string.Empty,
                minedDayIndex = Math.Max(0, dto.minedDayIndex),
                stockpileCategoryId = dto.stockpileCategoryId ?? string.Empty,
            };
        }
    }

    /// <summary>W8B: one FIFO dispense line out of the ore stock — what left and where it went.</summary>
    [Serializable]
    public sealed class MineOreDispenseLine
    {
        [SerializeField]
        private EntityId lotId = EntityId.Invalid;

        [SerializeField, Min(0)]
        private int tonsTaken;

        [SerializeField]
        private string destination = string.Empty;

        public EntityId LotId => lotId;
        public int TonsTaken => Math.Max(0, tonsTaken);
        public string Destination => destination ?? string.Empty;

        public MineOreDispenseLine() { }

        public MineOreDispenseLine(EntityId lotId, int tonsTaken, string destination)
        {
            this.lotId = lotId;
            this.tonsTaken = Math.Max(0, tonsTaken);
            this.destination = destination ?? string.Empty;
        }
    }

    /// <summary>
    /// W8B: the mine's ore stockpile — real lots in, FIFO tons out, nothing
    /// conjured. A lot is refused loudly unless it cites a shaft and a level
    /// (production must come from a tracked working, W8A).
    /// </summary>
    [Serializable]
    public sealed class MineOreStock
    {
        [SerializeField]
        private List<MineOreLot> lots = new List<MineOreLot>();

        /// <summary>W8B: assay results recorded against this stockpile's lots (persisted with the stock).</summary>
        [SerializeField]
        private List<MineAssayResult> assayResults = new List<MineAssayResult>();

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<MineOreLot> Lots => lots;
        public IReadOnlyList<MineAssayResult> AssayResults => assayResults;

        public MineOreStock() { }

        public int TotalTons
        {
            get
            {
                int total = 0;
                foreach (MineOreLot lot in lots)
                    total = Math.Max(0, total + lot.Tons);
                return total;
            }
        }

        public MineOreLot FindLot(EntityId lotId)
        {
            foreach (MineOreLot lot in lots)
            {
                if (lot.LotId.Equals(lotId))
                    return lot;
            }
            return null;
        }

        /// <summary>Records a completed assay against this stockpile's list (called by MineAssayService).</summary>
        public void AddAssayResult(MineAssayResult assay)
        {
            if (assay != null)
                assayResults.Add(assay);
        }

        /// <summary>Finds an assay by id.</summary>
        public MineAssayResult FindAssay(string assayId)
        {
            foreach (MineAssayResult assay in assayResults)
            {
                if (string.Equals(assay.AssayId, assayId, StringComparison.Ordinal))
                    return assay;
            }
            return null;
        }

        /// <summary>
        /// Receives a broken ore lot. Refuses orphan lots (no shaft/level
        /// provenance), zero tons, or invalid lot ids. Never conjures stock.
        /// </summary>
        public string ReceiveLot(MineOreLot lot, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (lot == null)
                return "MineOreStock.ReceiveLot: lot is required.";
            if (lot.LotId.Equals(EntityId.Invalid))
                return "MineOreStock.ReceiveLot: lot needs a real lot id — no anonymous ore.";
            if (lot.Tons <= 0)
                return "MineOreStock.ReceiveLot: lot must carry positive tons.";
            if (string.IsNullOrWhiteSpace(lot.ShaftId) || string.IsNullOrWhiteSpace(lot.LevelId))
                return "MineOreStock.ReceiveLot: ore must cite its shaft and level (tracked workings only) — no mystery ore.";
            if (FindLot(lot.LotId) != null)
                return $"MineOreStock.ReceiveLot: lot {lot.LotId.Id} already on the stockpile.";

            lots.Add(lot);
            return null;
        }

        /// <summary>
        /// Dispenses tons FIFO (oldest mined first). Refuses the whole request
        /// loudly when the stockpile cannot cover it — no partial conjuring.
        /// </summary>
        public List<MineOreDispenseLine> DispenseTons(int tons, string destination, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            var lines = new List<MineOreDispenseLine>();
            if (tons <= 0)
            {
                callerDiagnostics.Add("MineOreStock.DispenseTons: tons must be positive.");
                return lines;
            }
            if (TotalTons < tons)
            {
                callerDiagnostics.Add($"MineOreStock.DispenseTons: stockpile holds {TotalTons} tons, cannot dispense {tons} — refused, nothing moved.");
                return lines;
            }

            int remaining = tons;
            foreach (MineOreLot lot in lots)
            {
                if (remaining <= 0)
                    break;
                int removed = lot.RemoveTons(remaining);
                if (removed > 0)
                {
                    lines.Add(new MineOreDispenseLine(lot.LotId, removed, destination));
                    remaining -= removed;
                }
            }
            lots.RemoveAll(lot => lot.Tons <= 0);
            return lines;
        }

        public MineOreStockSaveDto CaptureSaveDto()
        {
            var dto = new MineOreStockSaveDto();
            foreach (MineOreLot lot in lots)
                dto.lots.Add(lot.CaptureSaveDto());
            foreach (MineAssayResult assay in assayResults)
                dto.assays.Add(assay.CaptureSaveDto());
            return dto;
        }

        public static MineOreStock FromSaveDto(MineOreStockSaveDto dto)
        {
            var stock = new MineOreStock();
            if (dto != null)
            {
                foreach (MineOreLotSaveDto lotDto in dto.lots)
                {
                    MineOreLot lot = MineOreLot.FromSaveDto(lotDto);
                    if (lot != null)
                        stock.lots.Add(lot);
                }
                foreach (MineAssayResultSaveDto assayDto in dto.assays)
                {
                    MineAssayResult assay = MineAssayResult.FromSaveDto(assayDto);
                    if (assay != null)
                        stock.assayResults.Add(assay);
                }
            }
            return stock;
        }
    }

    /// <summary>W8B: save DTOs for ore lots and the stockpile. Owned by the mine runtime (standing rule).</summary>
    [Serializable]
    public sealed class MineOreLotSaveDto
    {
        public int lotKind;
        public int lotSeq;
        public MineralResourceKind mineralKind;
        public int tons;
        public float gradeValue;
        public string gradeUnit = string.Empty;
        public bool assayed;
        public string assayId = string.Empty;
        public int assayerKind;
        public int assayerSeq;
        public int assayDayIndex = -1;
        public float gradeConfidence01 = 0.3f;
        public string shaftId = string.Empty;
        public string levelId = string.Empty;
        public string veinId = string.Empty;
        public int minedDayIndex;
        public string stockpileCategoryId = string.Empty;
    }

    [Serializable]
    public sealed class MineOreStockSaveDto
    {
        public List<MineOreLotSaveDto> lots = new List<MineOreLotSaveDto>();
        public List<MineAssayResultSaveDto> assays = new List<MineAssayResultSaveDto>();
    }
}
