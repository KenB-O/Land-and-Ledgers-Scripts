using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>
    /// D2B: the mill's saw blade as a REAL equipment asset — persistent
    /// identity, an owning business, procurement provenance, condition, and
    /// a maintenance history (Tech X §3.3: significant equipment uses the
    /// shared asset lifecycle when location, condition, ownership or
    /// maintenance matters; Canon Part V occupation table: the saw filer /
    /// mill maintenance role keeps "replacement saws").
    ///
    /// W4A had only an anonymous condition float; D2B gives the blade an
    /// identity and a supply chain: every blade is either bought from a NAMED
    /// supplier (merchant, machine shop, shipment — the source is named, the
    /// trade is never typed here) or an explicit BOOTSTRAP endowment (the
    /// opening-stock doctrine, cf. D1F/D2A fuel lots). Anonymous blades are
    /// refused loudly — a blade with no source is a claim, not inventory.
    ///
    /// Condition ladder (Tech X §3.9: condition gates, never debuffs):
    /// - keen: saws.
    /// - dull (at/below 0.35): the filer files it back (file-saw task).
    /// - broken (at/below 0.10): filing cannot fix it — the filer RE-SETS it
    ///   (re-set-saw task: saw set/swage tools, Canon occupation table) or
    ///   the mill fits a spare blade from inventory and retires the broken
    ///   one to scrap. A broken blade is never filed back to perfect.
    /// </summary>
    [Serializable]
    public sealed class SawmillBladeState
    {
        /// <summary>Condition at or below which the saw is dull: output gated until filed.</summary>
        public const float DullThreshold01 = 0.35f;
        /// <summary>Condition at or below which the blade is broken: re-set or replace, never just file.</summary>
        public const float BrokenThreshold01 = 0.10f;

        public string BladeId = string.Empty;
        public float Condition01 = 1f;
        public List<string> MaintenanceLog = new List<string>();

        // D2B: asset identity and procurement provenance.
        public string OwnerBusinessId = string.Empty; // the mill that owns this blade
        public string LocationId = string.Empty;      // physical truth — which mill yard it sits in
        public string ProcurementSource = string.Empty; // NAMED supplier, or "BOOTSTRAP endowment"
        public int ProcuredDayIndex = -1;
        public bool IsBootstrapEndowment; // true: opening stock, one-time; reorder via named suppliers
        public int ResetCount;            // times this blade has been re-set after breaking (factual history)
        public bool IsRetired;            // retired blades are scrap — never fitted, never filed
        public string RetiredReason = string.Empty;
        public int RetiredDayIndex = -1;

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
            if (IsRetired) return;
            Condition01 = Mathf.Clamp01(Condition01 - Mathf.Max(0f, amount01));
        }

        /// <summary>
        /// Files the saw back to a keen edge — the filer's work, at the
        /// mill's sharpening station (Canon Part V occupation table: files,
        /// sharpening station). Returns the refusal when there is nothing to
        /// file, the blade is broken (re-set or replace — filing cannot fix
        /// a broken saw), or the blade is retired scrap. Null when filed.
        /// </summary>
        public string RecordFiling(int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (string.IsNullOrWhiteSpace(BladeId))
                return "SawmillBladeState.RecordFiling: no blade id — nothing to file.";
            if (IsRetired)
                return $"SawmillBladeState.RecordFiling: blade '{BladeId}' is retired scrap ({RetiredReason}) — it is never filed.";
            if (IsBroken)
                return $"SawmillBladeState.RecordFiling: blade '{BladeId}' is BROKEN (condition {Condition01:0.00}) — "
                    + "filing cannot fix a broken saw. The filer must re-set it (re-set-saw) or the mill must fit a spare blade.";
            if (Condition01 >= 1f)
            {
                diag.Add($"SawmillBladeState: blade '{BladeId}' is already keen — filing skipped, no labor burned.");
                return null;
            }

            float before = Condition01;
            Condition01 = 1f;
            string entry = $"Day {dayIndex}: filed at the sharpening station (condition {before:0.00} -> 1.00)";
            MaintenanceLog.Add(entry);
            diag.Add($"SawmillBladeState: blade '{BladeId}' filed — {entry}.");
            return null;
        }

        /// <summary>
        /// D2B: re-sets a broken saw — the filer's heavier work with saw
        /// set/swage tools (Canon Part V occupation table). Only broken
        /// blades are re-set; dull blades are filed, keen blades need
        /// nothing. Returns the refusal, or null when re-set.
        /// </summary>
        public string RecordReset(int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (string.IsNullOrWhiteSpace(BladeId))
                return "SawmillBladeState.RecordReset: no blade id — nothing to re-set.";
            if (IsRetired)
                return $"SawmillBladeState.RecordReset: blade '{BladeId}' is retired scrap — it is never re-set.";
            if (!IsBroken)
                return $"SawmillBladeState.RecordReset: blade '{BladeId}' is not broken (condition {Condition01:0.00}) — "
                    + "re-setting is for broken saws; dull saws are filed.";

            float before = Condition01;
            Condition01 = 1f;
            ResetCount++;
            string entry = $"Day {dayIndex}: re-set with set/swage tools (condition {before:0.00} -> 1.00; re-set #{ResetCount})";
            MaintenanceLog.Add(entry);
            diag.Add($"SawmillBladeState: blade '{BladeId}' re-set — {entry}.");
            return null;
        }

        /// <summary>
        /// D2B: retires the blade to scrap with a recorded reason (cracked
        /// plate, lost teeth beyond setting, operator decision). Retired
        /// blades keep their identity and history for diligence/resale, but
        /// are never fitted, filed, or re-set again.
        /// </summary>
        public string Retire(string reason, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (IsRetired)
                return $"SawmillBladeState.Retire: blade '{BladeId}' is already retired ({RetiredReason}).";
            IsRetired = true;
            RetiredReason = string.IsNullOrWhiteSpace(reason) ? "retired" : reason;
            RetiredDayIndex = dayIndex;
            string entry = $"Day {dayIndex}: retired to scrap — {RetiredReason} (final condition {Condition01:0.00})";
            MaintenanceLog.Add(entry);
            diag.Add($"SawmillBladeState: blade '{BladeId}' {entry}.");
            return null;
        }

        /// <summary>
        /// The §3.9 gate: returns null when the blade may saw, otherwise the
        /// loud refusal. Broken is reported distinctly from dull so the
        /// player knows which work is needed; a missing or retired blade
        /// reports its own refusal.
        /// </summary>
        public string CheckSawGate(int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (string.IsNullOrWhiteSpace(BladeId))
                return "SawmillBladeState: no blade fitted — the saw line cannot run without its blade.";
            if (IsRetired)
                return $"SawmillBladeState: blade '{BladeId}' is retired scrap ({RetiredReason}) — fit a spare blade before sawing.";
            if (IsBroken)
            {
                string refusal = $"SawmillBladeState: blade '{BladeId}' is BROKEN (condition {Condition01:0.00}) — "
                    + "all sawing gated until the filer re-sets it (re-set-saw) or a spare blade is fitted (Tech X §3.9).";
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

        /// <summary>
        /// D2B: stamps procurement provenance on the blade. A blade needs a
        /// named supplier or an explicit bootstrap endowment — anonymous
        /// blades are refused (upstream-provenance doctrine).
        /// </summary>
        public string RecordProcurement(string source, int dayIndex, bool isBootstrapEndowment, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (string.IsNullOrWhiteSpace(source) && !isBootstrapEndowment)
                return $"SawmillBladeState.RecordProcurement: blade '{BladeId}' refused — name the supplier or declare the bootstrap endowment.";
            ProcurementSource = isBootstrapEndowment ? "BOOTSTRAP endowment" : source;
            ProcuredDayIndex = dayIndex;
            IsBootstrapEndowment = isBootstrapEndowment;
            diag.Add($"SawmillBladeState: blade '{BladeId}' procured — {ProcurementSource} (day {dayIndex}).");
            return null;
        }

        public string ProvenanceChain()
        {
            var parts = new List<string>();
            parts.Add($"blade {BladeId}");
            if (!string.IsNullOrWhiteSpace(ProcurementSource)) parts.Add(ProcurementSource);
            if (ProcuredDayIndex >= 0) parts.Add($"day {ProcuredDayIndex}");
            if (!string.IsNullOrWhiteSpace(OwnerBusinessId)) parts.Add($"owner {OwnerBusinessId}");
            if (IsRetired) parts.Add($"RETIRED: {RetiredReason}");
            return string.Join(" | ", parts.ToArray());
        }

        /// <summary>W4A save contract: lives inside the owning state class. D2B adds asset fields.</summary>
        [Serializable]
        public sealed class SawmillBladeStateSaveDto
        {
            public string BladeId = string.Empty;
            public float Condition01 = 1f;
            public List<string> MaintenanceLog = new List<string>();
            public string OwnerBusinessId = string.Empty;
            public string LocationId = string.Empty;
            public string ProcurementSource = string.Empty;
            public int ProcuredDayIndex = -1;
            public bool IsBootstrapEndowment;
            public int ResetCount;
            public bool IsRetired;
            public string RetiredReason = string.Empty;
            public int RetiredDayIndex = -1;
        }

        public SawmillBladeStateSaveDto CaptureSaveDto()
        {
            return new SawmillBladeStateSaveDto
            {
                BladeId = BladeId,
                Condition01 = Condition01,
                MaintenanceLog = new List<string>(MaintenanceLog),
                OwnerBusinessId = OwnerBusinessId,
                LocationId = LocationId,
                ProcurementSource = ProcurementSource,
                ProcuredDayIndex = ProcuredDayIndex,
                IsBootstrapEndowment = IsBootstrapEndowment,
                ResetCount = ResetCount,
                IsRetired = IsRetired,
                RetiredReason = RetiredReason,
                RetiredDayIndex = RetiredDayIndex,
            };
        }

        public void LoadFromSaveDto(SawmillBladeStateSaveDto dto)
        {
            if (dto == null)
            {
                BladeId = string.Empty;
                Condition01 = 1f;
                MaintenanceLog = new List<string>();
                OwnerBusinessId = string.Empty;
                LocationId = string.Empty;
                ProcurementSource = string.Empty;
                ProcuredDayIndex = -1;
                IsBootstrapEndowment = false;
                ResetCount = 0;
                IsRetired = false;
                RetiredReason = string.Empty;
                RetiredDayIndex = -1;
                return;
            }
            BladeId = dto.BladeId ?? string.Empty;
            Condition01 = Mathf.Clamp01(dto.Condition01);
            MaintenanceLog = new List<string>(dto.MaintenanceLog ?? new List<string>());
            OwnerBusinessId = dto.OwnerBusinessId ?? string.Empty;
            LocationId = dto.LocationId ?? string.Empty;
            ProcurementSource = dto.ProcurementSource ?? string.Empty;
            ProcuredDayIndex = dto.ProcuredDayIndex;
            IsBootstrapEndowment = dto.IsBootstrapEndowment;
            ResetCount = Math.Max(0, dto.ResetCount);
            IsRetired = dto.IsRetired;
            RetiredReason = dto.RetiredReason ?? string.Empty;
            RetiredDayIndex = dto.RetiredDayIndex;
        }
    }

    /// <summary>
    /// D2B: the mill's spare-blade inventory — real blade assets with
    /// identity and procurement provenance, never anonymous steel. Canon
    /// Part V occupation table: the saw filer keeps "replacement saws".
    /// Spares are fitted when the working blade breaks beyond filing or is
    /// retired; a shortfall (no spare on hand) gates the mill loudly and is
    /// never auto-ordered — reorder goes through named suppliers.
    /// </summary>
    public sealed class SawmillBladeInventory
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<SawmillBladeState> spares = new List<SawmillBladeState>();
        private readonly List<SawmillBladeState> scrap = new List<SawmillBladeState>();
        private readonly string millBusinessId;

        public SawmillBladeInventory(string millBusinessId)
        {
            this.millBusinessId = millBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<SawmillBladeState> Spares => spares;
        public IReadOnlyList<SawmillBladeState> Scrap => scrap;

        /// <summary>Receives a spare blade with procurement provenance. Returns the refusal, or null.</summary>
        public string ReceiveSpareBlade(SawmillBladeState blade, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (blade == null)
                return "SawmillBladeInventory.ReceiveSpareBlade: no blade offered — blades are not conjured.";
            if (string.IsNullOrWhiteSpace(blade.BladeId))
                return "SawmillBladeInventory.ReceiveSpareBlade: a blade needs an id — anonymous steel is refused.";
            if (blade.IsRetired)
                return $"SawmillBladeInventory.ReceiveSpareBlade: blade '{blade.BladeId}' is retired scrap — it is not a spare.";
            if (string.IsNullOrWhiteSpace(blade.ProcurementSource))
            {
                diag.Add($"SawmillBladeInventory: REFUSED blade '{blade.BladeId}' — no procurement source named. "
                    + "Anonymous blades are refused loudly: name the supplier or declare the bootstrap endowment.");
                return "SawmillBladeInventory.ReceiveSpareBlade: no procurement source — anonymous blades refused.";
            }
            foreach (var existing in spares)
            {
                if (string.Equals(existing.BladeId, blade.BladeId, StringComparison.Ordinal))
                    return $"SawmillBladeInventory.ReceiveSpareBlade: blade '{blade.BladeId}' is already a spare — double intake refused.";
            }

            blade.OwnerBusinessId = millBusinessId;
            blade.LocationId = millBusinessId;
            spares.Add(blade);
            diag.Add($"SawmillBladeInventory: spare blade received — {blade.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Takes the named spare blade for fitting. Returns null with a loud
        /// refusal when no such spare is on hand — the mill stays gated, and
        /// no blade is invented or auto-ordered.
        /// </summary>
        public SawmillBladeState TryTakeSpare(string bladeId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(bladeId))
            {
                diag.Add("SawmillBladeInventory: no spare blade named — nothing fitted.");
                return null;
            }
            foreach (var spare in spares)
            {
                if (string.Equals(spare.BladeId, bladeId, StringComparison.Ordinal))
                {
                    spares.Remove(spare);
                    diag.Add($"SawmillBladeInventory: spare blade '{bladeId}' taken for fitting.");
                    return spare;
                }
            }
            diag.Add($"SawmillBladeInventory: REFUSED — no spare blade '{bladeId}' on hand. "
                + "The mill stays gated until a blade is procured from a named supplier; nothing is auto-ordered.");
            return null;
        }

        /// <summary>Parks a blade as a spare (e.g. the previously fitted blade after a swap).</summary>
        public string ParkAsSpare(SawmillBladeState blade, List<string> diag)
        {
            return ReceiveSpareBlade(blade, diag);
        }

        /// <summary>Moves a retired blade to the scrap register — identity and history kept, never reused.</summary>
        public void MoveToScrap(SawmillBladeState blade, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (blade == null) return;
            spares.Remove(blade);
            if (!scrap.Contains(blade)) scrap.Add(blade);
            diag.Add($"SawmillBladeInventory: blade '{blade.BladeId}' moved to scrap — {blade.RetiredReason}.");
        }

        /// <summary>D2B save contract: lives inside the owning inventory class.</summary>
        [Serializable]
        public sealed class SawmillBladeInventorySaveDto
        {
            public List<SawmillBladeState.SawmillBladeStateSaveDto> Spares = new List<SawmillBladeState.SawmillBladeStateSaveDto>();
            public List<SawmillBladeState.SawmillBladeStateSaveDto> Scrap = new List<SawmillBladeState.SawmillBladeStateSaveDto>();
        }

        public SawmillBladeInventorySaveDto CaptureSaveDto()
        {
            var dto = new SawmillBladeInventorySaveDto();
            foreach (var blade in spares) dto.Spares.Add(blade.CaptureSaveDto());
            foreach (var blade in scrap) dto.Scrap.Add(blade.CaptureSaveDto());
            return dto;
        }

        public void LoadFromSaveDto(SawmillBladeInventorySaveDto dto)
        {
            spares.Clear();
            scrap.Clear();
            if (dto == null) return;
            foreach (var bladeDto in dto.Spares ?? new List<SawmillBladeState.SawmillBladeStateSaveDto>())
            {
                var blade = new SawmillBladeState();
                blade.LoadFromSaveDto(bladeDto);
                if (!string.IsNullOrWhiteSpace(blade.BladeId)) spares.Add(blade);
            }
            foreach (var bladeDto in dto.Scrap ?? new List<SawmillBladeState.SawmillBladeStateSaveDto>())
            {
                var blade = new SawmillBladeState();
                blade.LoadFromSaveDto(bladeDto);
                if (!string.IsNullOrWhiteSpace(blade.BladeId)) scrap.Add(blade);
            }
        }
    }

    /// <summary>
    /// W4A: the saw-filing maintenance task. The TTS-2 task id for the
    /// filer loop; the "saw-logs" task id stays owned by T1E
    /// (TimberChain.Sawmill.RegisterTaskDefinitions) — duplicates are
    /// rejected deterministically, so this package registers ONLY file-saw
    /// and re-set-saw.
    ///
    /// Filing needs the filer's touch on the mill's own saw line, so the
    /// task executes against the sawmill-saw-line workstation (the blade
    /// being filed is part of the workstation); the "saw-filing" repair
    /// capability lives as that workstation's support requirement (Canon
    /// 5.2, already authored in WorkstationCatalog.SawmillSawLine). Skill
    /// follows the T1E pattern: the forestry skill's "filing" aspect.
    ///
    /// D2B: the re-set-saw task — the filer's heavier work on a BROKEN saw
    /// with saw set/swage tools (Canon Part V occupation table: "Files; saw
    /// set/swage tools; vise/bench; gauges; sharpening station"). Filing
    /// sharpens; re-setting repairs. Sharpening as a blacksmith service is
    /// NOT implemented: the canon's dedicated saw-filer occupation with the
    /// mill's own equipment is the authority (D2B design fork, recorded).
    /// </summary>
    public static class SawmillMaintenanceTasks
    {
        public const string FileSawTaskId = "file-saw";
        public const string ReSetSawTaskId = "re-set-saw";

        /// <summary>Calibration: minutes for a filer to file and set one large circular saw.</summary>
        public const int FileSawMinutesPerBlade = 45;

        /// <summary>
        /// D2B calibration: minutes for a filer to re-set a broken saw with
        /// set/swage tools — heavier work than filing (package calibration,
        /// labeled as such; not a canon figure).
        /// </summary>
        public const int ReSetSawMinutesPerBlade = 120;

        public static void RegisterTaskDefinitions(TaskAuthority authority, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (authority == null)
            {
                diagnostics.Add("SawmillMaintenanceTasks: no TaskAuthority — file-saw / re-set-saw not registered.");
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

            var reset = new TaskDefinition(ReSetSawTaskId, "Re-set saw (set and swage)", ReSetSawMinutesPerBlade);
            reset.SetRequiredSkill(TimberHarvest.ForestrySkillId, new[] { "filing" });
            reset.EquipmentClasses.Add(EquipmentRequirementCodes.Workstation("sawmill-saw-line"));
            if (!authority.RegisterDefinition(reset, out rejection))
            {
                diagnostics.Add("SawmillMaintenanceTasks: re-set-saw registration rejected: " + rejection);
            }
        }
    }
}
