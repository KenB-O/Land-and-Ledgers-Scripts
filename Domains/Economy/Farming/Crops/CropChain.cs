using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// CRP-2: a harvested crop lot with provenance. Grain is storable — it does
    /// not rot in days like milk (Canon §7.4L: commodity-appropriate storage,
    /// not a global inventory pool). Storage needs are recorded, not simulated
    /// as spoilage here.
    /// </summary>
    [Serializable]
    public sealed class CropLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public CropKind Crop;
        public string ProductKind = string.Empty; // "grain", "sheaves", "hay", "corn-ears"
        public int QuantityUnits;
        public string FieldId = string.Empty;
        public string FarmId = string.Empty;
        public int HarvestDayIndex;
        public EntityId HarvestedBy = EntityId.Invalid; // worker person id (labor provenance)
        public string SeedSource = string.Empty;        // upstream provenance
        public string StorageNote = string.Empty;       // e.g. "needs dry granary"

        public CropLot() { }
    }

    /// <summary>
    /// CRP-2: the crop production authority. Every stage of the crop cycle is
    /// a TTS-2 task performed by a real person against their work-time budget
    /// (TTS-1/2): plowing, planting, tending, harvest, threshing. Nothing
    /// grows into inventory without harvest labor (Canon §7.4C).
    ///
    /// Seasonal windows come from FarmSeasons: planting happens in spring,
    /// harvest in late summer/fall. Work outside the window fails loudly.
    /// Missed work reduces realized production through the field state —
    /// never a flat Farm Efficiency percentage (Canon §7.4D).
    ///
    /// Calibration constants are tuning, not canon (Canon Part XV holds).
    /// </summary>
    public sealed class CropChain
    {
        public const string PlowFieldTaskId = "plow-field";
        public const string PlantFieldTaskId = "plant-field";
        public const string TendFieldTaskId = "tend-field";
        public const string HarvestFieldTaskId = "harvest-field";
        public const string ThreshGrainTaskId = "thresh-grain";
        public const string CutHayTaskId = "cut-hay";
        public const string StackHayTaskId = "stack-hay";
        /// <summary>CRP-3: milling task id (registered by Miller).</summary>
        public const string MillGrainTaskId = "mill-grain";

        // Calibration: minutes per acre at skill level 1 (TTS-1 minute quantum).
        public const int PlowMinutesPerAcre = 120;
        public const int PlantMinutesPerAcre = 60;
        public const int TendMinutesPerAcre = 45;
        public const int HarvestMinutesPerAcre = 150;
        public const int ThreshMinutesPerLot = 30;
        public const int CutHayMinutesPerAcre = 90;
        public const int StackHayMinutesPerAcre = 60;

        private CropFieldAuthority authority;
        private readonly Dictionary<string, CropLot> lots = new Dictionary<string, CropLot>();

        public CropChain(CropFieldAuthority fieldAuthority)
        {
            authority = fieldAuthority ?? new CropFieldAuthority();
        }

        /// <summary>The field authority backing this chain.</summary>
        public CropFieldAuthority Fields => authority;

        /// <summary>Registers the field-work task definitions (TTS-2).</summary>
        public static void RegisterTaskDefinitions(TaskAuthority taskAuthority)
        {
            if (taskAuthority == null) return;

            Register(taskAuthority, PlowFieldTaskId, "Plow field", PlowMinutesPerAcre,
                new[] { "plowing" }, "plow");
            Register(taskAuthority, PlantFieldTaskId, "Plant field", PlantMinutesPerAcre,
                new[] { "planting" }, "seed drill / hand sacks");
            Register(taskAuthority, TendFieldTaskId, "Tend growing crop", TendMinutesPerAcre,
                new[] { "cultivating" }, "hoe / cultivator");
            Register(taskAuthority, HarvestFieldTaskId, "Harvest field", HarvestMinutesPerAcre,
                new[] { "harvest" }, "scythe / cradle / wagon");
            Register(taskAuthority, ThreshGrainTaskId, "Thresh grain", ThreshMinutesPerLot,
                new[] { "threshing" }, "flail / threshing floor");
            Register(taskAuthority, CutHayTaskId, "Cut hay", CutHayMinutesPerAcre,
                new[] { "hay-cutting" }, "scythe / mower");
            Register(taskAuthority, StackHayTaskId, "Stack hay", StackHayMinutesPerAcre,
                new[] { "hay-stacking" }, "wagon / forks");
        }

        private static void Register(TaskAuthority taskAuthority, string id, string name,
            int minutesPerUnit, string[] tags, string equipment)
        {
            var def = new TaskDefinition(id, name, minutesPerUnit);
            def.SetRequiredSkill(SkillIds.CropTending, tags);
            def.EquipmentClasses.Add(equipment);
            taskAuthority.RegisterDefinition(def, out _);
        }

        /// <summary>
        /// Planting window: spring only. Planting outside the window is refused
        /// loudly — the season gate is real (Canon §7.4D seasonal capacity).
        /// </summary>
        public static bool InPlantingWindow(int dayIndex)
        {
            return FarmSeasons.SeasonForDayIndex(dayIndex) == FarmSeason.Spring;
        }

        /// <summary>
        /// Plants a prepared field: consumes seed (recorded with its source —
        /// no orphan inputs) and starts the growth clock. The caller assigns
        /// the plant-field task through TaskAuthority; this records the outcome.
        /// </summary>
        public string PlantField(
            string fieldId, CropKind crop, int seedUnits, string seedSource,
            EntityId workerId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            CropFieldState field = authority.GetField(fieldId);
            if (field == null) return $"CropChain.PlantField: unknown field '{fieldId}'.";
            if (field.GrowthState != CropGrowthState.Prepared)
            {
                return $"CropChain.PlantField: field '{fieldId}' is {field.GrowthState} — plow it first (Prepared).";
            }
            if (!InPlantingWindow(dayIndex))
            {
                return $"CropChain.PlantField: day {dayIndex} is {FarmSeasons.SeasonForDayIndex(dayIndex)}, not spring — planting outside the window is refused (Canon §7.4D).";
            }
            if (crop == CropKind.Unspecified)
            {
                return "CropChain.PlantField: no crop kind specified.";
            }
            if (seedUnits <= 0)
            {
                return $"CropChain.PlantField: planting {crop} on {field.Acres} acres needs seed — seed does not appear from nowhere.";
            }
            if (string.IsNullOrWhiteSpace(seedSource))
            {
                return "CropChain.PlantField: seed must name its source (upstream provenance — suppliers need sources too).";
            }

            field.CurrentCrop = crop;
            field.PlantedDayIndex = dayIndex;
            field.SeedSource = seedSource;
            field.Tended01 = 0f;
            if (crop == CropKind.Hay) field.HayStage = HayStage.Standing;

            string problem = authority.CompleteOperation(fieldId, CropGrowthState.Planted, diagnostics);
            if (problem != null) return problem;

            diagnostics.Add($"CropChain: planted {crop} on field '{fieldId}' ({field.Acres} acres) with {seedUnits} seed units from {seedSource}.");
            return null;
        }

        /// <summary>
        /// EQU-3: records a completed plowing. The plow-field TTS task may only be
        /// assigned when a <see cref="DraftPower.DraftWorkUnit"/> (team + plow +
        /// harness + driver) was assembled — this method enforces that the caller
        /// passes the worked unit, transitions the field to Prepared, and wears the
        /// implement. No unit, no plowing: the requirement is structural, not a hint.
        /// </summary>
        public string CompletePlowing(
            string fieldId,
            DraftPower.DraftWorkUnit unit,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            CropFieldState field = authority.GetField(fieldId);
            if (field == null) return $"CropChain.CompletePlowing: unknown field '{fieldId}'.";
            if (unit == null)
                return $"CropChain.CompletePlowing: field '{fieldId}' needs a draft work unit (team + plow + harness + driver) — no unit, no plowing (Canon: PlowField requires plow, draft power and harness).";
            if (unit.Implement == null || !string.Equals(unit.Implement.Kind, "plow", StringComparison.Ordinal))
                return $"CropChain.CompletePlowing: the unit's implement is not a plow — refused loudly.";
            if (unit.TeamAnimalIds == null || unit.TeamAnimalIds.Count < DraftPower.DraftWorkUnit.PlowTeamSize)
                return $"CropChain.CompletePlowing: the unit has no full horse team — no team, no plowing.";

            string problem = authority.CompleteOperation(fieldId, CropGrowthState.Prepared, diagnostics);
            if (problem != null) return problem;

            // Plowing wears the share (Tech X §3.9) — calibration rate.
            unit.Implement.ApplyWear(0.04f);
            diagnostics.Add(
                $"CropChain: plowed field '{fieldId}' with unit {unit.UnitId} " +
                $"({unit.TeamAnimalIds.Count} horses, driver {unit.DriverPersonId}) — " +
                $"plow {unit.Implement.AssetId} condition now {unit.Implement.Condition01:0.00}.");
            return null;
        }

        /// <summary>
        /// Tending (cultivation) during growth. Each tending pass raises the
        /// field's tended factor; untended fields yield less — missed work
        /// reduces through the field state (Canon §7.4D).
        /// </summary>
        public string TendField(string fieldId, EntityId workerId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            CropFieldState field = authority.GetField(fieldId);
            if (field == null) return $"CropChain.TendField: unknown field '{fieldId}'.";
            if (field.GrowthState != CropGrowthState.Growing && field.GrowthState != CropGrowthState.Planted)
            {
                return $"CropChain.TendField: field '{fieldId}' is {field.GrowthState} — nothing to tend.";
            }
            field.Tended01 = Mathf.Clamp01(field.Tended01 + 0.5f);
            if (field.GrowthState == CropGrowthState.Planted)
            {
                authority.CompleteOperation(fieldId, CropGrowthState.Growing, diagnostics);
            }
            diagnostics.Add($"CropChain: tended field '{fieldId}' (tended factor now {field.Tended01:P0}).");
            return null;
        }

        /// <summary>
        /// Harvests a ready field. Yield = acres × calibration × biological
        /// potential × work completion × seeded variance (Canon §7.4C: soil
        /// sets potential, work sets realization). Wheat/oats come off as
        /// sheaves needing threshing; corn as ears; hay enters its stage cycle.
        /// </summary>
        public CropLot HarvestField(
            EntityIdRegistry idRegistry,
            string fieldId, EntityId workerId, int dayIndex,
            float yieldVariance, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null) return null;
            CropFieldState field = authority.GetField(fieldId);
            if (field == null)
            {
                diagnostics.Add($"CropChain.HarvestField: unknown field '{fieldId}'.");
                return null;
            }
            if (field.GrowthState != CropGrowthState.Ready)
            {
                diagnostics.Add($"CropChain.HarvestField: field '{fieldId}' is {field.GrowthState} — harvest only ready fields.");
                return null;
            }

            CropCalibration calibration = authority.GetCalibration(field.CurrentCrop);
            if (calibration == null)
            {
                diagnostics.Add($"CropChain.HarvestField: no calibration for {field.CurrentCrop}.");
                return null;
            }

            // Hay does not harvest like grain — it enters the §7.4F stage cycle.
            if (field.CurrentCrop == CropKind.Hay)
            {
                diagnostics.Add($"CropChain.HarvestField: hay uses cut/cure/stack stages (Canon §7.4F), not grain harvest.");
                return null;
            }

            float potential = authority.BiologicalPotential(field);
            float workFactor = 0.5f + 0.5f * Mathf.Clamp01(field.Tended01);
            float variance = Mathf.Clamp(yieldVariance, 0.5f, 1.5f);
            int units = Mathf.RoundToInt(field.Acres * calibration.YieldUnitsPerAcre * potential * workFactor * variance);
            units = Math.Max(0, units);

            var lot = new CropLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                Crop = field.CurrentCrop,
                ProductKind = (field.CurrentCrop == CropKind.Wheat || field.CurrentCrop == CropKind.Oats) ? "sheaves" : "corn-ears",
                QuantityUnits = units,
                FieldId = fieldId,
                FarmId = field.FarmId,
                HarvestDayIndex = dayIndex,
                HarvestedBy = workerId,
                SeedSource = field.SeedSource,
                StorageNote = "sheaves need threshing before storage; keep dry",
            };
            lots[lot.LotId.ToString()] = lot;

            string problem = authority.CompleteOperation(fieldId, CropGrowthState.Harvested, diagnostics);
            if (problem != null)
            {
                diagnostics.Add(problem);
                return null;
            }

            diagnostics.Add($"CropChain: harvested {units} units of {field.CurrentCrop} from field '{fieldId}' " +
                $"(potential {potential:P0} × work {workFactor:P0} × variance {variance:P2}).");
            return lot;
        }

        /// <summary>
        /// Threshes sheaves into grain (Canon §7.4C: threshing/processing is
        /// work that realizes potential). Threshing loss is real but small.
        /// </summary>
        public CropLot ThreshGrain(
            EntityIdRegistry idRegistry,
            CropLot sheafLot, EntityId workerId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null) return null;
            if (sheafLot == null || sheafLot.QuantityUnits <= 0)
            {
                diagnostics.Add("CropChain.ThreshGrain: no sheaves to thresh.");
                return null;
            }
            if (sheafLot.ProductKind != "sheaves")
            {
                diagnostics.Add($"CropChain.ThreshGrain: lot {sheafLot.LotId} is {sheafLot.ProductKind}, not sheaves.");
                return null;
            }

            // Calibration: threshing recovers most of the sheaf weight as grain.
            const float threshRecovery = 0.9f;
            int grainUnits = Mathf.RoundToInt(sheafLot.QuantityUnits * threshRecovery);

            var grain = new CropLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                Crop = sheafLot.Crop,
                ProductKind = "grain",
                QuantityUnits = grainUnits,
                FieldId = sheafLot.FieldId,
                FarmId = sheafLot.FarmId,
                HarvestDayIndex = dayIndex,
                HarvestedBy = workerId,
                SeedSource = sheafLot.SeedSource,
                StorageNote = "grain: needs dry granary/elevator storage (Canon §7.4L)",
            };
            lots[grain.LotId.ToString()] = grain;
            sheafLot.QuantityUnits = 0; // consumed

            diagnostics.Add($"CropChain: threshed {grainUnits} grain units from lot {sheafLot.LotId} (10% threshing loss).");
            return grain;
        }

        /// <summary>
        /// Canon §7.4F: hay moves through physical stages — cut, curing, stack.
        /// Curing takes time and the crop can suffer if the stage work is
        /// missed; weather hooks in where a weather system exists.
        /// </summary>
        public string CutHay(string fieldId, EntityId workerId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            CropFieldState field = authority.GetField(fieldId);
            if (field == null) return $"CropChain.CutHay: unknown field '{fieldId}'.";
            if (field.CurrentCrop != CropKind.Hay || field.HayStage != HayStage.Standing)
            {
                return $"CropChain.CutHay: field '{fieldId}' has no standing hay to cut.";
            }
            field.HayStage = HayStage.Cut;
            field.HayCutDayIndex = dayIndex;
            diagnostics.Add($"CropChain: cut hay on field '{fieldId}' — now curing (Canon §7.4F).");
            return null;
        }

        /// <summary>
        /// Stacks cured hay into storage, producing hay lots for the feed loop.
        /// Hay must cure before stacking — rushing it is refused.
        /// </summary>
        public CropLot StackHay(
            EntityIdRegistry idRegistry,
            string fieldId, EntityId workerId, int dayIndex,
            float yieldVariance, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null) return null;
            CropFieldState field = authority.GetField(fieldId);
            if (field == null)
            {
                diagnostics.Add($"CropChain.StackHay: unknown field '{fieldId}'.");
                return null;
            }
            if (field.CurrentCrop != CropKind.Hay || (field.HayStage != HayStage.Cut && field.HayStage != HayStage.Curing))
            {
                diagnostics.Add($"CropChain.StackHay: field '{fieldId}' has no cut hay to stack.");
                return null;
            }

            // Calibration: hay needs ~3 days curing before stacking.
            const int cureDays = 3;
            if (dayIndex - field.HayCutDayIndex < cureDays)
            {
                diagnostics.Add($"CropChain.StackHay: hay on field '{fieldId}' needs {cureDays} curing days — rushed hay spoils in the stack (Canon §7.4F).");
                return null;
            }

            CropCalibration calibration = authority.GetCalibration(CropKind.Hay);
            float potential = authority.BiologicalPotential(field);
            float variance = Mathf.Clamp(yieldVariance, 0.5f, 1.5f);
            int units = Mathf.RoundToInt(field.Acres * calibration.YieldUnitsPerAcre * potential * variance);
            units = Math.Max(0, units);

            var lot = new CropLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                Crop = CropKind.Hay,
                ProductKind = "hay",
                QuantityUnits = units,
                FieldId = fieldId,
                FarmId = field.FarmId,
                HarvestDayIndex = dayIndex,
                HarvestedBy = workerId,
                SeedSource = field.SeedSource,
                StorageNote = "hay: dry stack/barn storage (Canon §7.4L)",
            };
            lots[lot.LotId.ToString()] = lot;

            field.HayStage = HayStage.Stacked;
            string problem = authority.CompleteOperation(fieldId, CropGrowthState.Harvested, diagnostics);
            if (problem != null) diagnostics.Add(problem);

            diagnostics.Add($"CropChain: stacked {units} hay units from field '{fieldId}'.");
            return lot;
        }

        public CropLot GetLot(string lotId)
        {
            if (string.IsNullOrWhiteSpace(lotId)) return null;
            CropLot lot;
            return lots.TryGetValue(lotId, out lot) ? lot : null;
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public CropChainSaveDto CaptureSaveDto()
        {
            return new CropChainSaveDto
            {
                fields = new List<CropFieldState>(authority.AllFields()),
                lots = new List<CropLot>(lots.Values),
            };
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(CropChainSaveDto dto)
        {
            lots.Clear();
            authority = new CropFieldAuthority(); // fresh authority; ids re-register stably
            if (dto == null) return;
            if (dto.fields != null)
            {
                foreach (var field in dto.fields)
                {
                    if (field != null && !string.IsNullOrWhiteSpace(field.FieldId))
                    {
                        // Re-register; ids are stable across save/load.
                        authority.RegisterField(field);
                    }
                }
            }
            if (dto.lots != null)
            {
                foreach (var lot in dto.lots)
                {
                    if (lot != null) lots[lot.LotId.ToString()] = lot;
                }
            }
        }
    }

    /// <summary>Save DTO for the crop chain (CLN-1 pattern).</summary>
    [Serializable]
    public sealed class CropChainSaveDto
    {
        public List<CropFieldState> fields = new List<CropFieldState>();
        public List<CropLot> lots = new List<CropLot>();
    }
}
