using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// CRP-1: starter crop kinds. Wheat and corn are the cash/food grains, oats
    /// and hay are the feed grains. The registry pattern is open: new kinds do
    /// not require code changes to the field authority, only calibration data.
    /// All yield/price numbers are calibration, not canon (Canon Part XV).
    /// </summary>
    public enum CropKind
    {
        Unspecified = 0,
        Wheat = 1, // cash grain; milling wheat → flour
        Corn = 2,  // feed + food grain
        Oats = 3,  // feed grain (horses, poultry)
        Hay = 4,   // winter forage (Canon §7.4F: first-class strategic commodity)
    }

    /// <summary>
    /// CRP-1: crop growth states. Canon §7.4C: a field carries real production
    /// state, not a farm-wide output meter. Failed exists because missed work
    /// and weather reduce realized production through the field state
    /// (Canon §7.4D) — never a flat Farm Efficiency percentage.
    /// </summary>
    public enum CropGrowthState
    {
        Unspecified = 0,
        Fallow = 1,     // resting; no crop in ground
        Prepared = 2,   // plowed/ground ready for seed
        Planted = 3,    // seed in ground, not yet emerged
        Growing = 4,    // emerged and developing
        Ready = 5,      // harvest window open
        Harvested = 6,  // crop taken off this cycle
        Failed = 7,     // lost: window missed, weather, or condition collapse
    }

    /// <summary>
    /// CRP-1: the field authority (Canon §7.4C). Cultivated land is represented
    /// by meaningful field/parcel production state: area, suitability/soil,
    /// moisture/water, current crop/use, current seasonal operation, work
    /// progress, crop condition, access, and ownership.
    ///
    /// Biological potential (soil, water, seed, weather, condition) is stored
    /// separately from work completion (operation + progress): soil determines
    /// what the land COULD produce; plowing/planting/tending/harvest determine
    /// how much of that potential is actually realized before windows close.
    /// A machine does not create yield; it lets the farm complete more work in
    /// time (Canon §7.4C).
    /// </summary>
    [Serializable]
    public sealed class CropFieldState
    {
        public string FieldId = string.Empty;      // stable id, e.g. "north-40"
        public string FarmId = string.Empty;       // owning farm (FVS-1 FarmDefinition.FarmId)
        public string LinkedPlotName = string.Empty; // FarmPlot (CropField/HayField) this field works
        public float Acres;

        // Biological potential (Canon §7.4C).
        public float Suitability01 = 0.7f;  // soil suitability for CurrentCrop (calibration)
        public float Moisture01 = 0.6f;     // water state (calibration)
        public float Condition01 = 1f;       // crop condition 0..1; weather/work move this

        // Current use and operation.
        public CropKind CurrentCrop = CropKind.Unspecified;
        public CropGrowthState GrowthState = CropGrowthState.Fallow;
        public string CurrentOperation = string.Empty; // e.g. "plowing", "planting", "tending", "harvest"
        public float WorkProgress01;                   // 0..1 progress on CurrentOperation

        // Provenance.
        public int PlantedDayIndex = -1;
        public string SeedSource = string.Empty;  // where the seed came from (no orphan inputs)
        /// <summary>
        /// W5C: the variety planted, as <see cref="CropVarietyCatalog"/> id(s)
        /// (joined with "+" when several lots were sown). SeedSource carries the
        /// human chain; this is the data link.
        /// </summary>
        public string SeedVarietyId = string.Empty;
        public string OwnerFarmId = string.Empty; // duplicate of FarmId for parcel-level clarity

        // Hay staging (Canon §7.4F): hay has physical stages beyond the grain
        // cycle — standing, cut, curing, stacked. Grain crops ignore this.
        public HayStage HayStage = HayStage.NotHay;
        public int HayCutDayIndex = -1; // day the hay was cut (curing clock)

        // CRP-2: tending factor 0..1 — each tending pass raises realized yield.
        // Missed work reduces through the field state (Canon §7.4D).
        public float Tended01;

        public CropFieldState() { }

        public CropFieldState(string fieldId, string farmId, float acres)
        {
            FieldId = fieldId ?? string.Empty;
            FarmId = farmId ?? string.Empty;
            OwnerFarmId = FarmId;
            Acres = Math.Max(0f, acres);
        }
    }

    /// <summary>Canon §7.4F: hay moves through physical stages with timing that
    /// matters — cutting, drying/curing, collection/stacking, storage.</summary>
    public enum HayStage
    {
        NotHay = 0,
        Standing = 1,  // grown, not yet cut
        Cut = 2,       // mown, lying in the field
        Curing = 3,    // drying; weather matters here
        Stacked = 4,   // collected into stacks/barn storage
    }

    /// <summary>
    /// CRP-1: calibration data per crop kind. Canon fixes the structure
    /// (Canon §7.4C–§7.4F, Part XV holds); every number below is gameplay
    /// tuning, explicitly not a historical constant.
    /// </summary>
    [Serializable]
    public sealed class CropCalibration
    {
        public CropKind Kind;
        public int YieldUnitsPerAcre = 20;   // at suitability 1.0, condition 1.0, full work
        public int SeedUnitsPerAcre = 2;     // seed consumed at planting
        public int GrowDays = 90;            // planted → ready (calibration)
        public int HarvestWindowDays = 21;   // ready → failed if unharvested
        public bool IsHay;                   // hay uses the hay-stage cycle instead

        public CropCalibration() { }

        public CropCalibration(CropKind kind, int yieldPerAcre, int seedPerAcre,
            int growDays, int harvestWindowDays, bool isHay = false)
        {
            Kind = kind;
            YieldUnitsPerAcre = yieldPerAcre;
            SeedUnitsPerAcre = seedPerAcre;
            GrowDays = growDays;
            HarvestWindowDays = harvestWindowDays;
            IsHay = isHay;
        }
    }

    /// <summary>
    /// CRP-1: the crop field authority. Owns field registration, legal state
    /// transitions, and the biological-potential read model. Work tasks
    /// (CRP-2) drive transitions through this authority — nothing grows,
    /// ripens, or harvests itself.
    /// </summary>
    public sealed class CropFieldAuthority
    {
        private readonly Dictionary<string, CropFieldState> fields =
            new Dictionary<string, CropFieldState>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<CropKind, CropCalibration> calibrations =
            new Dictionary<CropKind, CropCalibration>();

        public CropFieldAuthority()
        {
            // Starter calibrations (tuning, not canon).
            calibrations[CropKind.Wheat] = new CropCalibration(CropKind.Wheat, 22, 2, 95, 21);
            calibrations[CropKind.Corn] = new CropCalibration(CropKind.Corn, 30, 1, 100, 28);
            calibrations[CropKind.Oats] = new CropCalibration(CropKind.Oats, 28, 3, 85, 21);
            calibrations[CropKind.Hay] = new CropCalibration(CropKind.Hay, 40, 1, 60, 30, isHay: true);
        }

        public void SetCalibration(CropCalibration calibration)
        {
            if (calibration == null || calibration.Kind == CropKind.Unspecified) return;
            calibrations[calibration.Kind] = calibration;
        }

        public CropCalibration GetCalibration(CropKind kind)
        {
            CropCalibration calibration;
            return calibrations.TryGetValue(kind, out calibration) ? calibration : null;
        }

        /// <summary>Registers a field from a farm plot. Returns a diagnostic on rejection.</summary>
        public string RegisterField(CropFieldState field)
        {
            if (field == null) return "CropFieldAuthority: no field supplied.";
            if (string.IsNullOrWhiteSpace(field.FieldId)) return "CropFieldAuthority: field needs a stable FieldId.";
            if (field.Acres <= 0f) return $"CropFieldAuthority: field '{field.FieldId}' has no acres.";
            if (fields.ContainsKey(field.FieldId))
            {
                return $"CropFieldAuthority: field '{field.FieldId}' is already registered — ids are never reused.";
            }
            fields[field.FieldId] = field;
            return null;
        }

        public CropFieldState GetField(string fieldId)
        {
            if (string.IsNullOrWhiteSpace(fieldId)) return null;
            CropFieldState field;
            return fields.TryGetValue(fieldId, out field) ? field : null;
        }

        public IEnumerable<CropFieldState> AllFields() => fields.Values;

        /// <summary>
        /// Canon §7.4C: biological potential of a field — what the land COULD
        /// produce given soil, water, and crop condition. Work completion
        /// (CRP-2) determines how much of this is realized. 0..1 multiplier.
        /// </summary>
        public float BiologicalPotential(CropFieldState field)
        {
            if (field == null) return 0f;
            if (field.GrowthState == CropGrowthState.Fallow
                || field.GrowthState == CropGrowthState.Failed
                || field.GrowthState == CropGrowthState.Harvested)
            {
                return 0f;
            }
            float suitability = Mathf.Clamp01(field.Suitability01);
            float moisture = Mathf.Clamp01(field.Moisture01);
            float condition = Mathf.Clamp01(field.Condition01);
            // Water-limited: moisture below 0.3 drags potential down hard.
            float waterFactor = moisture < 0.3f ? moisture / 0.3f * 0.5f : 0.5f + moisture * 0.5f;
            return suitability * waterFactor * condition;
        }

        /// <summary>
        /// Advances growth by day for planted/growing crops. Ready when the
        /// calibration grow-days elapse; unharvested crops fail when the
        /// harvest window closes (Canon §7.4D: missed work reduces through the
        /// field state). Returns ids of fields that changed state.
        /// </summary>
        public List<string> AdvanceDay(int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var changed = new List<string>();
            foreach (var field in fields.Values)
            {
                if (field.CurrentCrop == CropKind.Unspecified) continue;
                CropCalibration calibration = GetCalibration(field.CurrentCrop);
                if (calibration == null) continue;

                if (field.GrowthState == CropGrowthState.Planted
                    || field.GrowthState == CropGrowthState.Growing)
                {
                    field.GrowthState = CropGrowthState.Growing;
                    int daysPlanted = dayIndex - field.PlantedDayIndex;
                    if (daysPlanted >= calibration.GrowDays)
                    {
                        field.GrowthState = CropGrowthState.Ready;
                        changed.Add(field.FieldId);
                        diagnostics.Add($"CropFieldAuthority: field '{field.FieldId}' ({field.CurrentCrop}) is ready for harvest.");
                    }
                }
                else if (field.GrowthState == CropGrowthState.Ready)
                {
                    int daysPlanted = dayIndex - field.PlantedDayIndex;
                    if (daysPlanted >= calibration.GrowDays + calibration.HarvestWindowDays)
                    {
                        field.GrowthState = CropGrowthState.Failed;
                        changed.Add(field.FieldId);
                        diagnostics.Add($"CropFieldAuthority: field '{field.FieldId}' ({field.CurrentCrop}) missed its harvest window — crop failed (Canon §7.4D).");
                    }
                }
            }
            return changed;
        }

        /// <summary>Legal transitions driven by completed work tasks (CRP-2).</summary>
        public string BeginOperation(string fieldId, string operation)
        {
            CropFieldState field = GetField(fieldId);
            if (field == null) return $"CropFieldAuthority: unknown field '{fieldId}'.";
            field.CurrentOperation = operation ?? string.Empty;
            field.WorkProgress01 = 0f;
            return null;
        }

        public string CompleteOperation(string fieldId, CropGrowthState newState, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            CropFieldState field = GetField(fieldId);
            if (field == null) return $"CropFieldAuthority: unknown field '{fieldId}'.";
            if (!IsLegalTransition(field.GrowthState, newState))
            {
                return $"CropFieldAuthority: illegal transition {field.GrowthState} → {newState} on field '{fieldId}' — work must follow the crop cycle.";
            }
            field.GrowthState = newState;
            field.WorkProgress01 = 1f;
            field.CurrentOperation = string.Empty;
            return null;
        }

        public static bool IsLegalTransition(CropGrowthState from, CropGrowthState to)
        {
            switch (from)
            {
                case CropGrowthState.Fallow: return to == CropGrowthState.Prepared;
                case CropGrowthState.Prepared: return to == CropGrowthState.Planted || to == CropGrowthState.Fallow;
                case CropGrowthState.Planted: return to == CropGrowthState.Growing;
                case CropGrowthState.Growing: return to == CropGrowthState.Ready;
                case CropGrowthState.Ready: return to == CropGrowthState.Harvested || to == CropGrowthState.Failed;
                case CropGrowthState.Harvested: return to == CropGrowthState.Fallow || to == CropGrowthState.Prepared;
                case CropGrowthState.Failed: return to == CropGrowthState.Fallow || to == CropGrowthState.Prepared;
                default: return false;
            }
        }
    }
}
