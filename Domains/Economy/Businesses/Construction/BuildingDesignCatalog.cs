using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Phase D (Real People): one construction phase inside a building design.
    /// DATA — materials, labor and tools are recorded requirements, never
    /// a rigid house-upgrade tech tree. Each phase becomes one
    /// <c>WorkPackage</c> at project creation.
    /// </summary>
    [Serializable]
    public sealed class BuildingDesignPhase
    {
        public string PhaseName = string.Empty;
        public int Sequence;
        public List<ConstructionMaterialRequirement> Materials = new List<ConstructionMaterialRequirement>();
        public int LaborMinutes;
        /// <summary>
        /// TUNING: tool item ids the crew must hold (e.g. "saw", "hammer",
        /// "level"). The design names them; the executor verifies custody.
        /// </summary>
        public List<string> RequiredToolItemIds = new List<string>();
        public string Notes = string.Empty;

        public BuildingDesignPhase() { }
    }

    /// <summary>Phase D: one accommodation space a completed design yields.</summary>
    [Serializable]
    public sealed class BuildingDesignSpaceSpec
    {
        public string Label = string.Empty;
        public int SleepingCapacity;
        public string Notes = string.Empty;

        public BuildingDesignSpaceSpec() { }
    }

    /// <summary>
    /// Phase D: a legitimate building design — configurable catalog DATA.
    /// A household (or builder) chooses a design; the project executor then
    /// creates real WorkPackages, stages real materials and commits real
    /// person-time. Designs never conjure buildings.
    /// </summary>
    [Serializable]
    public sealed class BuildingDesign
    {
        public string DesignId = string.Empty;
        public string DisplayName = string.Empty;
        public ConstructionWorksKind WorksKind = ConstructionWorksKind.Unspecified;
        /// <summary>Building kind label registered with the HousingAuthority (e.g. "house", "cabin").</summary>
        public string KindLabel = string.Empty;
        public List<BuildingDesignPhase> Phases = new List<BuildingDesignPhase>();
        public List<BuildingDesignSpaceSpec> SpaceSpecs = new List<BuildingDesignSpaceSpec>();
        /// <summary>
        /// Unity prefab id the visual placement needs. Empty = no prefab
        /// required (data-only structure or a Codex-authored generic). When
        /// non-empty and unavailable in this repo, construction proceeds as
        /// DATA and a Codex handoff record is filed — visual placement is
        /// never claimed from GitHub.
        /// </summary>
        public string RequiredPrefabId = string.Empty;
        /// <summary>
        /// The code contract the prefab must expose (footprint anchors,
        /// door/window sockets, snap points) — the exact Codex handoff.
        /// </summary>
        public List<string> PrefabExposedContract = new List<string>();

        public BuildingDesign() { }

        public int TotalLaborMinutes()
        {
            int total = 0;
            foreach (BuildingDesignPhase phase in Phases)
                if (phase != null) total += Math.Max(0, phase.LaborMinutes);
            return total;
        }
    }

    /// <summary>Phase D: prefab handoff lifecycle. Append-only.</summary>
    public enum PrefabHandoffStatus
    {
        PendingCodex = 0,
        AcknowledgedByCodex = 1,
    }

    /// <summary>
    /// Phase D: the exact Codex handoff for a prefab-dependent design —
    /// WHICH prefab, WHAT it must expose. Filed when a design requires a
    /// prefab unavailable in this scripts repo. No visual placement is
    /// claimed until Codex acknowledges and integrates it in Unity.
    /// </summary>
    [Serializable]
    public sealed class PrefabHandoffRecord
    {
        public string RecordId = string.Empty;
        public string DesignId = string.Empty;
        public string RequiredPrefabId = string.Empty;
        public List<string> ExposedContract = new List<string>();
        public PrefabHandoffStatus Status = PrefabHandoffStatus.PendingCodex;
        public int CreatedDayIndex;
        public string Notes = string.Empty;

        public PrefabHandoffRecord() { }
    }

    /// <summary>
    /// Phase D: the building-design catalog — DATA, registered by scenario
    /// or by Codex as designs are authored. The catalog also tracks which
    /// prefab ids exist in the Unity project (Codex registers them) and
    /// files handoff records for designs whose prefabs are missing.
    /// </summary>
    public sealed class BuildingDesignCatalog
    {
        private readonly Dictionary<string, BuildingDesign> designs =
            new Dictionary<string, BuildingDesign>(StringComparer.Ordinal);
        private readonly HashSet<string> availablePrefabs =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly List<PrefabHandoffRecord> handoffs = new List<PrefabHandoffRecord>();
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<PrefabHandoffRecord> Handoffs => handoffs;

        public string RegisterDesign(BuildingDesign design, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (design == null || string.IsNullOrWhiteSpace(design.DesignId))
                return "BuildingDesignCatalog.RegisterDesign: a design needs an id.";
            if (design.Phases == null || design.Phases.Count == 0)
                return $"BuildingDesignCatalog.RegisterDesign: design '{design.DesignId}' names no phases — a design without phases builds nothing.";
            if (design.SpaceSpecs == null || design.SpaceSpecs.Count == 0)
                return $"BuildingDesignCatalog.RegisterDesign: design '{design.DesignId}' yields no accommodation spaces.";
            if (designs.ContainsKey(design.DesignId))
                return $"BuildingDesignCatalog.RegisterDesign: design '{design.DesignId}' already registered.";
            designs[design.DesignId] = design;
            diag.Add($"BuildingDesignCatalog: design '{design.DesignId}' ('{design.DisplayName}') registered — {design.Phases.Count} phase(s), {design.SpaceSpecs.Count} space(s).");
            return null;
        }

        public BuildingDesign FindDesign(string designId)
        {
            if (string.IsNullOrWhiteSpace(designId)) return null;
            designs.TryGetValue(designId, out BuildingDesign design);
            return design;
        }

        /// <summary>
        /// Phase F: read-only enumeration of registered designs, for NPC
        /// design selection. The catalog remains the single design truth;
        /// this adds no second writer.
        /// </summary>
        public List<BuildingDesign> AllDesigns()
        {
            return new List<BuildingDesign>(designs.Values);
        }

        /// <summary>Phase D: Codex registers prefab ids that exist in the Unity project.</summary>
        public void RegisterAvailablePrefab(string prefabId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(prefabId)) return;
            availablePrefabs.Add(prefabId);
            diag.Add($"BuildingDesignCatalog: prefab '{prefabId}' registered as available.");
        }

        public bool IsPrefabAvailable(string prefabId)
        {
            return !string.IsNullOrWhiteSpace(prefabId) && availablePrefabs.Contains(prefabId);
        }

        /// <summary>
        /// Phase D: files the exact Codex handoff when a design's required
        /// prefab is unavailable. Returns the record, or null when no
        /// handoff is needed.
        /// </summary>
        public PrefabHandoffRecord RequestPrefabHandoff(BuildingDesign design, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (design == null || string.IsNullOrWhiteSpace(design.RequiredPrefabId)) return null;
            if (IsPrefabAvailable(design.RequiredPrefabId)) return null;
            foreach (PrefabHandoffRecord existing in handoffs)
            {
                if (existing != null
                    && string.Equals(existing.DesignId, design.DesignId, StringComparison.Ordinal)
                    && string.Equals(existing.RequiredPrefabId, design.RequiredPrefabId, StringComparison.Ordinal)
                    && existing.Status == PrefabHandoffStatus.PendingCodex)
                    return existing;
            }
            var record = new PrefabHandoffRecord
            {
                RecordId = $"pho-{sequence++}",
                DesignId = design.DesignId,
                RequiredPrefabId = design.RequiredPrefabId,
                ExposedContract = new List<string>(design.PrefabExposedContract ?? new List<string>()),
                Status = PrefabHandoffStatus.PendingCodex,
                CreatedDayIndex = dayIndex,
                Notes = $"Design '{design.DisplayName}' needs Unity prefab '{design.RequiredPrefabId}' — " +
                    "construction proceeds as data; visual placement is Codex's job in Unity, never claimed from GitHub.",
            };
            handoffs.Add(record);
            diag.Add($"BuildingDesignCatalog: Codex handoff '{record.RecordId}' filed — prefab '{design.RequiredPrefabId}' missing for design '{design.DesignId}'.");
            return record;
        }

        #region Save / Load
        [Serializable]
        public sealed class BuildingDesignCatalogSaveDto
        {
            public List<BuildingDesign> Designs = new List<BuildingDesign>();
            public List<string> AvailablePrefabs = new List<string>();
            public List<PrefabHandoffRecord> Handoffs = new List<PrefabHandoffRecord>();
        }

        public BuildingDesignCatalogSaveDto CaptureSaveDto()
        {
            var dto = new BuildingDesignCatalogSaveDto();
            foreach (BuildingDesign design in designs.Values)
                if (design != null) dto.Designs.Add(design);
            dto.AvailablePrefabs.AddRange(availablePrefabs);
            dto.Handoffs.AddRange(handoffs);
            return dto;
        }

        public void LoadFromSaveDto(BuildingDesignCatalogSaveDto dto)
        {
            designs.Clear();
            availablePrefabs.Clear();
            handoffs.Clear();
            if (dto == null) return;
            foreach (BuildingDesign design in dto.Designs ?? new List<BuildingDesign>())
            {
                if (design == null || string.IsNullOrWhiteSpace(design.DesignId)) continue;
                if (designs.ContainsKey(design.DesignId))
                {
                    diagnostics.Add($"LoadFromSaveDto: duplicate design '{design.DesignId}' skipped.");
                    continue;
                }
                designs[design.DesignId] = design;
            }
            foreach (string prefabId in dto.AvailablePrefabs ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(prefabId)) availablePrefabs.Add(prefabId);
            handoffs.AddRange(dto.Handoffs ?? new List<PrefabHandoffRecord>());
        }
        #endregion
    }
}
