using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>
    /// W4A: the mill's saw blade — the physical asset whose wear gates
    /// output. Tech X §3.9 / the EQP-2 filer loop: a dull/broken saw gates
    /// ALL output until the filer works; condition gates, never debuffs
    /// (NX-1A / Canon 4.1). Wear accrues per log sawn from the conversion
    /// data; filing restores the edge and records the event (diligence and
    /// resale history, cf. EquipmentAsset.RecordMaintenance).
    ///
    /// Calibration as data-thresholds on the type: a blade dulls at 0.35
    /// and is broken at 0.10 — a dull blade still turns but tears stock,
    /// so output stops at dull, not at destruction.
    /// </summary>
    [Serializable]
    public sealed class SawmillBladeState
    {
        /// <summary>Condition at or below which the saw is dull: output gated until filed.</summary>
        public const float DullThreshold01 = 0.35f;
        /// <summary>Condition at or below which the blade is broken: needs re-setting, not just filing.</summary>
        public const float BrokenThreshold01 = 0.10f;

        public string BladeId = string.Empty;
        public float Condition01 = 1f;
        public List<string> MaintenanceLog = new List<string>();

        public SawmillBladeState() { }

        public SawmillBladeState(string bladeId)
        {
            BladeId = bladeId ?? string.Empty;
        }

        public bool IsDull => Condition01 <= DullThreshold01;
        public bool IsBroken => Condition01 <= BrokenThreshold01;

        /// <summary>Applies wear per log sawn. Wear is data-driven (SawmillConversionProfile.BladeWearPerLog01).</summary>
        public void ApplyWear(float amount01)
        {
            Condition01 = Mathf.Clamp01(Condition01 - Mathf.Max(0f, amount01));
        }

        /// <summary>
        /// Files the saw back to a keen edge — the filer's work. Returns the
        /// refusal when there is nothing to file, or null.
        /// </summary>
        public string RecordFiling(int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (string.IsNullOrWhiteSpace(BladeId))
                return "SawmillBladeState.RecordFiling: no blade id — nothing to file.";
            if (Condition01 >= 1f)
            {
                diag.Add($"SawmillBladeState: blade '{BladeId}' is already keen — filing skipped, no labor burned.");
                return null;
            }

            float before = Condition01;
            Condition01 = 1f;
            string entry = $"Day {dayIndex}: filed (condition {before:0.00} -> 1.00)";
            MaintenanceLog.Add(entry);
            diag.Add($"SawmillBladeState: blade '{BladeId}' filed — {entry}.");
            return null;
        }

        /// <summary>
        /// The §3.9 gate: returns null when the blade may saw, otherwise the
        /// loud refusal. Broken is reported distinctly from dull so the
        /// player knows which work is needed.
        /// </summary>
        public string CheckSawGate(int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (string.IsNullOrWhiteSpace(BladeId))
                return "SawmillBladeState: no blade fitted — the saw line cannot run without its blade.";
            if (IsBroken)
            {
                string refusal = $"SawmillBladeState: blade '{BladeId}' is BROKEN (condition {Condition01:0.00}) — "
                    + "all sawing gated until the filer re-sets it (Tech X §3.9).";
                diag.Add(refusal);
                return refusal;
            }
            if (IsDull)
            {
                string refusal = $"SawmillBladeState: blade '{BladeId}' is DULL (condition {Condition01:0.00}) — "
                    + "all sawing gated until the filer works (Tech X §3.9).";
                diag.Add(refusal);
                return refusal;
            }
            return null;
        }

        /// <summary>W4A save contract: lives inside the owning state class.</summary>
        [Serializable]
        public sealed class SawmillBladeStateSaveDto
        {
            public string BladeId = string.Empty;
            public float Condition01 = 1f;
            public List<string> MaintenanceLog = new List<string>();
        }

        public SawmillBladeStateSaveDto CaptureSaveDto()
        {
            return new SawmillBladeStateSaveDto
            {
                BladeId = BladeId,
                Condition01 = Condition01,
                MaintenanceLog = new List<string>(MaintenanceLog),
            };
        }

        public void LoadFromSaveDto(SawmillBladeStateSaveDto dto)
        {
            if (dto == null)
            {
                BladeId = string.Empty;
                Condition01 = 1f;
                MaintenanceLog = new List<string>();
                return;
            }
            BladeId = dto.BladeId ?? string.Empty;
            Condition01 = Mathf.Clamp01(dto.Condition01);
            MaintenanceLog = new List<string>(dto.MaintenanceLog ?? new List<string>());
        }
    }

    /// <summary>
    /// W4A: the saw-filing maintenance task. The TTS-2 task id for the
    /// filer loop; the "saw-logs" task id stays owned by T1E
    /// (TimberChain.Sawmill.RegisterTaskDefinitions) — duplicates are
    /// rejected deterministically, so this package registers ONLY file-saw.
    ///
    /// Filing needs the filer's touch on the mill's own saw line, so the
    /// task executes against the sawmill-saw-line workstation (the blade
    /// being filed is part of the workstation); the "saw-filing" repair
    /// capability lives as that workstation's support requirement (Canon
    /// 5.2, already authored in WorkstationCatalog.SawmillSawLine). Skill
    /// follows the T1E pattern: the forestry skill's "filing" aspect.
    /// </summary>
    public static class SawmillMaintenanceTasks
    {
        public const string FileSawTaskId = "file-saw";

        /// <summary>Calibration: minutes for a filer to file and set one large circular saw.</summary>
        public const int FileSawMinutesPerBlade = 45;

        public static void RegisterTaskDefinitions(TaskAuthority authority, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (authority == null)
            {
                diagnostics.Add("SawmillMaintenanceTasks: no TaskAuthority — file-saw not registered.");
                return;
            }

            var file = new TaskDefinition(FileSawTaskId, "File saw", FileSawMinutesPerBlade);
            file.SetRequiredSkill(TimberHarvest.ForestrySkillId, new[] { "filing" });
            file.EquipmentClasses.Add(EquipmentRequirementCodes.Workstation("sawmill-saw-line"));
            string rejection;
            if (!authority.RegisterDefinition(file, out rejection))
            {
                diagnostics.Add("SawmillMaintenanceTasks: file-saw registration rejected: " + rejection);
            }
        }
    }
}
