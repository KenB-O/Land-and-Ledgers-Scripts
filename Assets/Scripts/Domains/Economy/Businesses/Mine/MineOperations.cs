using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// D3C: mine operating posture per Canon 21.7L — "development-heavy,
    /// normal production, reduced operation, temporary idle or closure
    /// rather than a simple on/off switch." Posture is an owner decision
    /// recorded on the mine; it does not itself change physics.
    /// </summary>
    public enum MineOperatingPosture
    {
        Unspecified = 0,
        DevelopmentHeavy = 1, // capital going into opening ground ahead of output
        NormalProduction = 2, // steady working
        ReducedOperation = 3, // cut back — fewer shifts/fronts, costs trimmed
        TemporarilyIdle = 4,  // no production; preservation burdens continue
        Closed = 5,           // shut — restart is a fresh decision, not a toggle
    }

    /// <summary>
    /// D3C: the preservation burden of an idle or closed mine (Canon 21.7L:
    /// "Idling may still require pumping, inspection, security, maintenance
    /// or preservation"). All quantities are caller-set — the mine's actual
    /// water exposure and site needs decide them, never a default schedule.
    /// </summary>
    [Serializable]
    public sealed class MinePreservationBurden
    {
        /// <summary>Pump-operator shifts per week to hold water while idle.</summary>
        [SerializeField, Min(0)]
        private int pumpShiftsPerWeek;

        /// <summary>Days between inspection walk-throughs while idle.</summary>
        [SerializeField, Min(0)]
        private int inspectionIntervalDays;

        /// <summary>Watchman/security shifts per week while idle.</summary>
        [SerializeField, Min(0)]
        private int securityShiftsPerWeek;

        [SerializeField, TextArea(1, 2)]
        private string note = string.Empty;

        public int PumpShiftsPerWeek => Math.Max(0, pumpShiftsPerWeek);
        public int InspectionIntervalDays => Math.Max(0, inspectionIntervalDays);
        public int SecurityShiftsPerWeek => Math.Max(0, securityShiftsPerWeek);
        public string Note => note ?? string.Empty;

        public MinePreservationBurden() { }

        public MinePreservationBurden(int pumpShiftsPerWeek, int inspectionIntervalDays,
            int securityShiftsPerWeek, string note)
        {
            this.pumpShiftsPerWeek = Math.Max(0, pumpShiftsPerWeek);
            this.inspectionIntervalDays = Math.Max(0, inspectionIntervalDays);
            this.securityShiftsPerWeek = Math.Max(0, securityShiftsPerWeek);
            this.note = note ?? string.Empty;
        }

        public bool HasBurden => PumpShiftsPerWeek > 0 || InspectionIntervalDays > 0 || SecurityShiftsPerWeek > 0;

        public string Describe()
        {
            if (!HasBurden)
                return "No preservation burden recorded — an idle wet mine with no pumping is a restart risk (Canon 21.7L).";
            return $"Preservation while idle: {PumpShiftsPerWeek} pump shifts/week, " +
                $"inspection every {InspectionIntervalDays} days, {SecurityShiftsPerWeek} security shifts/week." +
                (string.IsNullOrWhiteSpace(Note) ? string.Empty : $" Note: {Note}");
        }

        public MinePreservationBurdenSaveDto CaptureSaveDto()
        {
            return new MinePreservationBurdenSaveDto
            {
                pumpShiftsPerWeek = PumpShiftsPerWeek,
                inspectionIntervalDays = InspectionIntervalDays,
                securityShiftsPerWeek = SecurityShiftsPerWeek,
                note = Note,
            };
        }

        public static MinePreservationBurden FromSaveDto(MinePreservationBurdenSaveDto dto)
        {
            if (dto == null)
                return new MinePreservationBurden();
            return new MinePreservationBurden(dto.pumpShiftsPerWeek, dto.inspectionIntervalDays,
                dto.securityShiftsPerWeek, dto.note ?? string.Empty);
        }
    }

    /// <summary>D3C: save DTO for the preservation burden. Owned by the mine runtime.</summary>
    [Serializable]
    public sealed class MinePreservationBurdenSaveDto
    {
        public int pumpShiftsPerWeek;
        public int inspectionIntervalDays;
        public int securityShiftsPerWeek;
        public string note = string.Empty;
    }

    /// <summary>
    /// D3C: owner/superintendent allocation among work fronts (Canon 21.7D:
    /// "North Workings can be well understood and expensive to support while
    /// a deep extension remains uncertain and water-prone. Owner/
    /// superintendent allocation among fronts becomes a strategic decision").
    /// Shares are recorded per level; only levels worked for production
    /// (InOre) may take a share, and shares may not exceed the whole.
    /// </summary>
    [Serializable]
    public sealed class MineWorkFrontAllocation
    {
        [Serializable]
        private sealed class FrontShare
        {
            public string levelId = string.Empty;

            [Range(0f, 1f)]
            public float share01;
        }

        [SerializeField]
        private List<FrontShare> shares = new List<FrontShare>();

        public MineWorkFrontAllocation() { }

        /// <summary>Share of working effort on a level, 0-1. Refuses non-producing levels and over-allocation.</summary>
        public string SetAllocation(string levelId, float share01, MineShaftPlan plan, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(levelId))
            {
                callerDiagnostics.Add("MineWorkFrontAllocation.SetAllocation: level id is required.");
                return "MineWorkFrontAllocation.SetAllocation: level id is required.";
            }
            if (share01 <= 0f || share01 > 1f)
            {
                callerDiagnostics.Add("MineWorkFrontAllocation.SetAllocation: share must be in (0, 1].");
                return "MineWorkFrontAllocation.SetAllocation: share must be in (0, 1].";
            }
            MineLevel level = plan != null ? plan.FindLevel(levelId) : null;
            if (level == null)
            {
                callerDiagnostics.Add($"MineWorkFrontAllocation.SetAllocation: level '{levelId}' is not on the mine plan.");
                return $"MineWorkFrontAllocation.SetAllocation: level '{levelId}' is not on the mine plan.";
            }
            if (level.WorkingStatus != MineWorkingStatus.InOre)
            {
                callerDiagnostics.Add($"MineWorkFrontAllocation.SetAllocation: level '{levelId}' is {level.WorkingStatus}, not worked for production (InOre).");
                return $"MineWorkFrontAllocation.SetAllocation: level '{levelId}' is not a producing front.";
            }

            float others = 0f;
            foreach (FrontShare existing in shares)
            {
                if (!string.Equals(existing.levelId, levelId, StringComparison.Ordinal))
                    others += existing.share01;
            }
            if (others + share01 > 1f + 1e-6f)
            {
                callerDiagnostics.Add($"MineWorkFrontAllocation.SetAllocation: shares would total {others + share01:P0} — over-allocated.");
                return "MineWorkFrontAllocation.SetAllocation: total shares may not exceed 100%.";
            }

            shares.RemoveAll(s => string.Equals(s.levelId, levelId, StringComparison.Ordinal));
            shares.Add(new FrontShare { levelId = levelId, share01 = Mathf.Clamp01(share01) });
            return null;
        }

        public void ClearAllocation(string levelId)
        {
            shares.RemoveAll(s => string.Equals(s.levelId, levelId, StringComparison.Ordinal));
        }

        public float ShareFor(string levelId)
        {
            foreach (FrontShare share in shares)
            {
                if (string.Equals(share.levelId, levelId, StringComparison.Ordinal))
                    return Mathf.Clamp01(share.share01);
            }
            return 0f;
        }

        public float TotalAllocated
        {
            get
            {
                float total = 0f;
                foreach (FrontShare share in shares)
                    total += share.share01;
                return Mathf.Clamp01(total);
            }
        }

        public MineWorkFrontAllocationSaveDto CaptureSaveDto()
        {
            var dto = new MineWorkFrontAllocationSaveDto();
            foreach (FrontShare share in shares)
            {
                dto.levelIds.Add(share.levelId);
                dto.shares01.Add(Mathf.Clamp01(share.share01));
            }
            return dto;
        }

        public static MineWorkFrontAllocation FromSaveDto(MineWorkFrontAllocationSaveDto dto)
        {
            var allocation = new MineWorkFrontAllocation();
            if (dto != null)
            {
                int count = Math.Min(dto.levelIds.Count, dto.shares01.Count);
                for (int i = 0; i < count; i++)
                {
                    allocation.shares.Add(new FrontShare
                    {
                        levelId = dto.levelIds[i] ?? string.Empty,
                        share01 = Mathf.Clamp01(dto.shares01[i]),
                    });
                }
            }
            return allocation;
        }
    }

    /// <summary>D3C: save DTO for work-front allocation. Owned by the mine runtime.</summary>
    [Serializable]
    public sealed class MineWorkFrontAllocationSaveDto
    {
        public List<string> levelIds = new List<string>();
        public List<float> shares01 = new List<float>();
    }

    public static class MineOperatingPostureNames
    {
        public static string GetDisplayName(MineOperatingPosture posture)
        {
            return posture switch
            {
                MineOperatingPosture.DevelopmentHeavy => "Development-heavy",
                MineOperatingPosture.NormalProduction => "Normal production",
                MineOperatingPosture.ReducedOperation => "Reduced operation",
                MineOperatingPosture.TemporarilyIdle => "Temporarily idle",
                MineOperatingPosture.Closed => "Closed",
                _ => "Unset",
            };
        }
    }
}
