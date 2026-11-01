using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Risk
{
    /// <summary>NX-2B: the disaster kinds the world can inflict.</summary>
    public enum DisasterKind
    {
        Unspecified = 0,
        Grasshoppers = 1,
        PrairieFire = 2,
        Drought = 3, // developing shock — see DroughtState
    }

    /// <summary>
    /// NX-2B: developing-shock stages (Canon 9.6F). Drought moves
    /// Early → Mounting → Action; recovery only through real rain.
    /// </summary>
    public enum DroughtStage
    {
        None = 0,
        Early = 1,
        Mounting = 2,
        Action = 3,
    }

    /// <summary>NX-2B: one drought episode's state (per region/farm group).</summary>
    [Serializable]
    public sealed class DroughtState
    {
        public string RegionId = string.Empty;
        public DroughtStage Stage = DroughtStage.None;
        public int DeficitDays; // consecutive dry days
        public int StartDayIndex = -1;

        public DroughtState() { }
    }

    /// <summary>NX-2B: save data for the agricultural risk service.</summary>
    [Serializable]
    public sealed class AgriculturalRiskSaveDto
    {
        public List<string> fireguards = new List<string>();
        public List<DroughtState> droughts = new List<DroughtState>();
    }

    /// <summary>NX-2B: what a disaster did — honest accounting, not a multiplier.</summary>
    [Serializable]
    public sealed class DisasterReport
    {
        public DisasterKind Kind;
        public int DayIndex;
        public List<string> AffectedFieldIds = new List<string>();
        public float AcresDestroyed;
        public int FieldsFailed;
        public string Notes = string.Empty;

        public DisasterReport() { }
    }

    /// <summary>
    /// NX-2B: agricultural disasters operate on ACTUAL state (Canon §9.1, §9.3).
    /// Grasshoppers consume actual crop/forage state. Prairie fire uses
    /// fuel/weather/ignition/mitigation and is reduced by real fireguards.
    /// Drought is a causal chain: yield → feed shortage → condition/culling →
    /// prices/cash pressure → debt/migration (Canon §9.3) — realized through the
    /// existing systems (CRP-2 yield math reads Moisture01; the feed loop already
    /// drops condition on shortfall). Do not model shocks as flat multipliers.
    ///
    /// All frequencies/severities are calibration per Canon Part XV. Historical
    /// basis (researched): Rocky Mountain locust plagues devastated Dakota/
    /// Minnesota wheat 1873–1877; prairie fires were a constant Plains hazard,
    /// fought with plowed fireguards; the mid-1870s drought + grasshoppers drove
    /// the hardship/abandonment the canon cites in §9.2.
    /// </summary>
    public sealed class AgriculturalRiskService
    {
        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        // Fireguards: plowed breaks recorded per farm+field (Canon §9.3:
        // prairie fire "can be reduced by real fireguards").
        private readonly HashSet<string> fireguards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string FireguardKey(string farmId, string fieldId) => $"{farmId}|{fieldId}";

        // Tracked drought episodes by region (for the save path).
        private readonly Dictionary<string, DroughtState> droughts =
            new Dictionary<string, DroughtState>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets or creates the tracked drought episode for a region.</summary>
        public DroughtState GetOrCreateDrought(string regionId)
        {
            string key = string.IsNullOrWhiteSpace(regionId) ? "local" : regionId;
            if (!droughts.TryGetValue(key, out DroughtState state))
            {
                state = new DroughtState { RegionId = key };
                droughts[key] = state;
            }
            return state;
        }

        /// <summary>
        /// Advances the tracked regional drought one day. Convenience over
        /// GetOrCreateDrought + AdvanceDrought for the game loop.
        /// </summary>
        public DroughtState AdvanceRegionDrought(
            string regionId, float rainfall01, CropFieldAuthority crops,
            int dayIndex, List<string> diag)
        {
            DroughtState state = GetOrCreateDrought(regionId);
            AdvanceDrought(state, rainfall01, crops, dayIndex, diag);
            return state;
        }

        /// <summary>
        /// Records a real plowed fireguard around a field. Built as field work
        /// (the caller schedules the plowing task); the guard only exists once
        /// recorded here. Reduces — never eliminates — fire spread to the field.
        /// </summary>
        public void RecordFireguard(string farmId, string fieldId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(farmId) || string.IsNullOrWhiteSpace(fieldId))
            {
                diag.Add("AgriculturalRiskService.RecordFireguard: farm and field are required — no phantom fireguards.");
                return;
            }
            fireguards.Add(FireguardKey(farmId, fieldId));
            diag.Add($"AgriculturalRiskService: fireguard recorded around field '{fieldId}' (farm {farmId}), day {dayIndex}.");
        }

        public bool HasFireguard(string farmId, string fieldId)
        {
            return fireguards.Contains(FireguardKey(farmId, fieldId));
        }

        /// <summary>
        /// NX-2B: grasshopper outbreak. Consumes ACTUAL crop/forage state:
        /// growing/ready fields lose condition; severe defoliation fails the
        /// field outright. Hay fields are forage — hoppers eat them too.
        /// Seeded and farm-local: only the listed farms are touched.
        /// </summary>
        public DisasterReport TriggerGrasshoppers(
            List<string> farmIds, CropFieldAuthority crops,
            float severity01, int seed, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var report = new DisasterReport { Kind = DisasterKind.Grasshoppers, DayIndex = dayIndex };
            if (farmIds == null || crops == null)
            {
                report.Notes = "no farms or no crop authority — no outbreak.";
                return report;
            }
            severity01 = Mathf.Clamp01(severity01);
            var rng = new System.Random(seed * 1013904223 + dayIndex);

            foreach (CropFieldState field in crops.AllFields())
            {
                if (field == null || !farmIds.Contains(field.FarmId)) continue;
                if (field.GrowthState != CropGrowthState.Growing && field.GrowthState != CropGrowthState.Ready)
                    continue; // nothing to eat — fallow/plowed/harvested fields are untouched

                // Hoppers eat the actual state: condition collapses first, then the stand.
                float damage = severity01 * (0.5f + (float)rng.NextDouble() * 0.5f);
                field.Condition01 = Mathf.Clamp01(field.Condition01 - damage);
                report.AffectedFieldIds.Add(field.FieldId);
                if (field.Condition01 <= 0.15f)
                {
                    field.GrowthState = CropGrowthState.Failed;
                    field.Condition01 = 0f;
                    report.AcresDestroyed += field.Acres;
                    report.FieldsFailed++;
                }
            }
            report.Notes = $"grasshoppers (severity {severity01:0.00}): {report.AffectedFieldIds.Count} fields damaged, " +
                $"{report.FieldsFailed} failed ({report.AcresDestroyed:0.0} ac). Historical: Rocky Mountain locust, 1873–77.";
            diag.Add($"AgriculturalRiskService: {report.Notes}");
            return report;
        }

        /// <summary>
        /// NX-2B: prairie fire. Inputs: fuel load (dry forage/grass 0..1),
        /// wind (mph), and ignition (the caller decides ignition honestly — a
        /// seeded lightning/stray-spark event, never a fiat "fire happens").
        /// Fields with recorded fireguards are shielded (reduced, not immune).
        /// Burns actual state: fields go to Failed, forage is consumed.
        /// </summary>
        public DisasterReport TriggerPrairieFire(
            List<string> farmIds, CropFieldAuthority crops,
            float fuelLoad01, float windMph, int seed, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var report = new DisasterReport { Kind = DisasterKind.PrairieFire, DayIndex = dayIndex };
            if (farmIds == null || crops == null)
            {
                report.Notes = "no farms or no crop authority — no fire.";
                return report;
            }
            fuelLoad01 = Mathf.Clamp01(fuelLoad01);
            var rng = new System.Random(seed * 1013904223 + dayIndex * 7 + 1);

            // Spread probability rises with fuel and wind (calibration).
            float spreadP = Mathf.Clamp01(0.25f + fuelLoad01 * 0.5f + Mathf.Min(0.25f, windMph / 120f));

            foreach (CropFieldState field in crops.AllFields())
            {
                if (field == null || !farmIds.Contains(field.FarmId)) continue;
                if (field.GrowthState != CropGrowthState.Growing && field.GrowthState != CropGrowthState.Ready)
                    continue;

                bool guarded = HasFireguard(field.FarmId, field.FieldId);
                float p = guarded ? spreadP * 0.25f : spreadP; // fireguards reduce, never eliminate (Canon §9.3)
                if (rng.NextDouble() < p)
                {
                    field.GrowthState = CropGrowthState.Failed;
                    field.Condition01 = 0f;
                    report.AffectedFieldIds.Add(field.FieldId);
                    report.AcresDestroyed += field.Acres;
                    report.FieldsFailed++;
                    if (guarded)
                        diag.Add($"AgriculturalRiskService: fire breached the fireguard at '{field.FieldId}' — guards reduce, not prevent.");
                }
            }
            report.Notes = $"prairie fire (fuel {fuelLoad01:0.00}, wind {windMph:0.0} mph): " +
                $"{report.FieldsFailed} fields burned ({report.AcresDestroyed:0.0} ac).";
            diag.Add($"AgriculturalRiskService: {report.Notes}");
            return report;
        }

        /// <summary>
        /// NX-2B: advances one drought episode. rainfall01 is the day's real
        /// precipitation (0 = none); the caller owns the weather. Dry days
        /// accumulate; wet days relieve. Effects are applied to actual field
        /// moisture — the CRP-2 yield math reads Moisture01, so lower moisture
        /// honestly lowers yield, which honestly tightens feed (canon chain).
        /// </summary>
        public DroughtState AdvanceDrought(
            DroughtState state, float rainfall01, CropFieldAuthority crops,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (state == null) state = new DroughtState { RegionId = "local" };
            rainfall01 = Mathf.Clamp01(rainfall01);

            if (rainfall01 >= 0.4f)
            {
                // Real rain breaks the deficit.
                if (state.Stage != DroughtStage.None)
                    diag.Add($"AgriculturalRiskService: rain ({rainfall01:0.00}) breaks the drought in '{state.RegionId}' — recovery is real, not timed.");
                state.Stage = DroughtStage.None;
                state.DeficitDays = 0;
                return state;
            }

            state.DeficitDays++;
            if (state.StartDayIndex < 0) state.StartDayIndex = dayIndex;
            DroughtStage next = state.DeficitDays switch
            {
                < 14 => DroughtStage.Early,
                < 35 => DroughtStage.Mounting,
                _ => DroughtStage.Action,
            };
            if (next != state.Stage)
            {
                state.Stage = next;
                diag.Add($"AgriculturalRiskService: drought in '{state.RegionId}' → {next} " +
                    $"(day {state.DeficitDays} of deficit, Canon 9.6F developing shock).");
            }

            // Apply to actual moisture state. Severity grows with stage.
            if (crops != null && state.Stage != DroughtStage.None)
            {
                float drain = state.Stage switch
                {
                    DroughtStage.Early => 0.01f,
                    DroughtStage.Mounting => 0.03f,
                    DroughtStage.Action => 0.06f,
                    _ => 0f,
                };
                foreach (CropFieldState field in crops.AllFields())
                {
                    if (field == null) continue;
                    field.Moisture01 = Mathf.Clamp01(field.Moisture01 - drain);
                }
            }
            return state;
        }

        public AgriculturalRiskSaveDto CaptureSaveDto()
        {
            return new AgriculturalRiskSaveDto
            {
                fireguards = new List<string>(fireguards),
                droughts = new List<DroughtState>(droughts.Values),
            };
        }

        public void LoadFromSaveDto(AgriculturalRiskSaveDto dto)
        {
            fireguards.Clear();
            droughts.Clear();
            if (dto == null) return;
            if (dto.fireguards != null)
                foreach (string key in dto.fireguards)
                    if (!string.IsNullOrEmpty(key)) fireguards.Add(key);
            if (dto.droughts != null)
                foreach (DroughtState state in dto.droughts)
                    if (state != null && !string.IsNullOrEmpty(state.RegionId))
                        droughts[state.RegionId] = state;
        }
    }
}
