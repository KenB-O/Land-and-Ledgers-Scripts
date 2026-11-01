using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Crops;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainMill
{
    /// <summary>
    /// W5A: the mill's stones — the physical asset whose wear gates output.
    /// Tech X §3.9 / the NX-1 equipment-gate pattern: a dull (needs
    /// dressing) or broken (cracked/spalled) pair of stones gates ALL
    /// grinding until the stone dresser works; condition gates, never
    /// debuffs (NX-1A / Canon 4.1). Wear accrues per grain unit ground from
    /// the conversion data; dressing restores the cutting face and records
    /// the event (diligence and resale history, cf. EquipmentAsset.
    /// RecordMaintenance). Historically the stones were "dressed" (recut
    /// with a mill bill) — a dull stone still turns but bolts badly, so
    /// output stops at dull, not at destruction.
    ///
    /// Calibration as data-thresholds on the type: stones need dressing at
    /// 0.35 and are broken at 0.10.
    /// </summary>
    [Serializable]
    public sealed class GrainMillStoneState
    {
        /// <summary>Condition at or below which the stones are dull: output gated until dressed.</summary>
        public const float DullThreshold01 = 0.35f;
        /// <summary>Condition at or below which the stones are broken (cracked/spalled): need re-cutting, not just dressing.</summary>
        public const float BrokenThreshold01 = 0.10f;

        public string StoneId = string.Empty;
        public float Condition01 = 1f;
        public List<string> MaintenanceLog = new List<string>();

        public GrainMillStoneState() { }

        public GrainMillStoneState(string stoneId)
        {
            StoneId = stoneId ?? string.Empty;
        }

        public bool IsDull => Condition01 <= DullThreshold01;
        public bool IsBroken => Condition01 <= BrokenThreshold01;

        /// <summary>Applies wear per grain unit ground. Wear is data-driven (GristMillConversionProfile.StoneWearPer100Units01).</summary>
        public void ApplyWear(float amount01)
        {
            Condition01 = Mathf.Clamp01(Condition01 - Mathf.Max(0f, amount01));
        }

        /// <summary>
        /// Dresses the stones back to a sharp cutting face — the stone
        /// dresser's work. Returns the refusal when there is nothing to
        /// dress, or null.
        /// </summary>
        public string RecordDressing(EntityId dresser, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (string.IsNullOrWhiteSpace(StoneId))
                return "GrainMillStoneState.RecordDressing: no stone id — nothing to dress.";
            if (Condition01 >= 1f)
            {
                diag.Add($"GrainMillStoneState: stones '{StoneId}' are already sharp — dressing skipped, no labor burned.");
                return null;
            }

            float before = Condition01;
            Condition01 = 1f;
            string entry = $"Day {dayIndex}: dressed by {dresser} (condition {before:0.00} -> 1.00)";
            MaintenanceLog.Add(entry);
            diag.Add($"GrainMillStoneState: stones '{StoneId}' dressed — {entry}.");
            return null;
        }

        /// <summary>
        /// The §3.9 gate: returns null when the stones may grind, otherwise
        /// the loud refusal. Broken is reported distinctly from dull so the
        /// player knows which work is needed.
        /// </summary>
        public string CheckStoneGate(int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (string.IsNullOrWhiteSpace(StoneId))
                return "GrainMillStoneState: no stones fitted — the mill cannot grind without its stones.";
            if (IsBroken)
            {
                string refusal = $"GrainMillStoneState: stones '{StoneId}' are BROKEN (condition {Condition01:0.00}) — "
                    + "all grinding gated until the dresser re-cuts them (Tech X §3.9).";
                diag.Add(refusal);
                return refusal;
            }
            if (IsDull)
            {
                string refusal = $"GrainMillStoneState: stones '{StoneId}' are DULL (condition {Condition01:0.00}) — "
                    + "all grinding gated until the dresser works (Tech X §3.9).";
                diag.Add(refusal);
                return refusal;
            }
            return null;
        }

        /// <summary>W5A save contract: lives inside the owning state class.</summary>
        [Serializable]
        public sealed class GrainMillStoneStateSaveDto
        {
            public string StoneId = string.Empty;
            public float Condition01 = 1f;
            public List<string> MaintenanceLog = new List<string>();
        }

        public GrainMillStoneStateSaveDto CaptureSaveDto()
        {
            return new GrainMillStoneStateSaveDto
            {
                StoneId = StoneId,
                Condition01 = Condition01,
                MaintenanceLog = new List<string>(MaintenanceLog),
            };
        }

        public void LoadFromSaveDto(GrainMillStoneStateSaveDto dto)
        {
            if (dto == null)
            {
                StoneId = string.Empty;
                Condition01 = 1f;
                MaintenanceLog = new List<string>();
                return;
            }
            StoneId = dto.StoneId ?? string.Empty;
            Condition01 = Mathf.Clamp01(dto.Condition01);
            MaintenanceLog = new List<string>(dto.MaintenanceLog ?? new List<string>());
        }
    }

    /// <summary>
    /// W5A: the stone-dressing maintenance task. The TTS-2 task id for the
    /// dresser loop; the "mill-grain" task id stays owned by CRP-3
    /// (Miller.RegisterTaskDefinitions / CropChain.MillGrainTaskId) —
    /// duplicates are rejected deterministically, so this package registers
    /// ONLY dress-millstones.
    ///
    /// Dressing happens at the mill's own grain-mill-station (the stones are
    /// part of the workstation); the "stone-dressing" repair aspect lives as
    /// the station's support requirements (Canon 5.2, already authored in
    /// WorkstationCatalog.GrainMillStation). Skill follows the CRP-3
    /// extension path: the milling skill's "stone-dressing" aspect.
    /// </summary>
    public static class GrainMillMaintenanceTasks
    {
        public const string DressStonesTaskId = "dress-millstones";

        /// <summary>Calibration: minutes for a dresser to dress one pair of stones.</summary>
        public const int DressStonesMinutesPerPair = 45;

        public static void RegisterTaskDefinitions(TaskAuthority authority, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (authority == null)
            {
                diagnostics.Add("GrainMillMaintenanceTasks: no TaskAuthority — dress-millstones not registered.");
                return;
            }

            var dress = new TaskDefinition(DressStonesTaskId, "Dress millstones", DressStonesMinutesPerPair);
            dress.SetRequiredSkill(Miller.MillingSkillId, new[] { "stone-dressing" });
            dress.EquipmentClasses.Add(EquipmentRequirementCodes.Workstation("grain-mill-station"));
            string rejection;
            if (!authority.RegisterDefinition(dress, out rejection))
            {
                diagnostics.Add("GrainMillMaintenanceTasks: dress-millstones registration rejected: " + rejection);
            }
        }
    }
}
