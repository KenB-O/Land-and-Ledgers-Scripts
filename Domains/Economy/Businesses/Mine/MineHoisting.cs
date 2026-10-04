using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// W8D: hoisting equipment by scale (Canon Part V hoist-operator
    /// profile: "Windlass/horse/steam hoist according to scale"). A gin is
    /// the intermediate horse-powered drum between windlass and full steam.
    /// </summary>
    public enum HoistKind
    {
        Unspecified = 0,
        Windlass = 1,   // hand windlass — sinking stage
        HorseWhim = 2,  // horse-powered whim
        Gin = 3,        // horse gin — deeper shafts
        SteamHoist = 4, // steam-powered hoist — production scale
    }

    /// <summary>
    /// W8D: one installed hoisting plant as real equipment with condition.
    /// Canon Part V: rope/cable, bucket/cage, brake/control apparatus, and
    /// inspection/oiling tools. Rope wears with tons hoisted and must be
    /// replaced; general condition wears and must be repaired. A hoist in
    /// poor condition cannot hoist (condition gate, Tech X §3.9) — it does
    /// not silently debuff output.
    /// </summary>
    [Serializable]
    public sealed class MineHoist
    {
        [SerializeField]
        private string hoistId = string.Empty;

        [SerializeField]
        private HoistKind hoistKind = HoistKind.Unspecified;

        [SerializeField]
        private string shaftId = string.Empty;

        [SerializeField, Min(0)]
        private int installedDayIndex;

        [SerializeField, Range(0f, 1f)]
        private float condition01 = 1f;

        /// <summary>Rope/cable condition — the wear item (Canon: "rope/cable").</summary>
        [SerializeField, Range(0f, 1f)]
        private float ropeCondition01 = 1f;

        [SerializeField, Min(-1)]
        private int lastInspectionDayIndex = -1;

        [SerializeField]
        private bool inService = true;

        public string HoistId => hoistId ?? string.Empty;
        public HoistKind HoistKind => hoistKind;
        public string ShaftId => shaftId ?? string.Empty;
        public int InstalledDayIndex => Math.Max(0, installedDayIndex);
        public float Condition01 => Mathf.Clamp01(condition01);
        public float RopeCondition01 => Mathf.Clamp01(ropeCondition01);
        public int LastInspectionDayIndex => lastInspectionDayIndex;
        public bool InService => inService;

        /// <summary>Canon-anchored gate: hoisting needs serviceable plant and rope (Tech X §3.9 condition gate).</summary>
        public bool CanHoist => inService && Condition01 >= 0.3f && RopeCondition01 >= 0.15f;

        public bool RopeNeedsReplacement => RopeCondition01 < 0.25f;

        public MineHoist() { }

        public MineHoist(string hoistId, HoistKind hoistKind, string shaftId, int installedDayIndex)
        {
            this.hoistId = hoistId ?? string.Empty;
            this.hoistKind = hoistKind;
            this.shaftId = shaftId ?? string.Empty;
            this.installedDayIndex = Math.Max(0, installedDayIndex);
        }

        public static string GetHoistKindDisplayName(HoistKind kind)
        {
            return kind switch
            {
                HoistKind.Windlass => "Windlass",
                HoistKind.HorseWhim => "Horse whim",
                HoistKind.Gin => "Horse gin",
                HoistKind.SteamHoist => "Steam hoist",
                _ => "Hoist",
            };
        }

        /// <summary>TUNING (calibration): rope wear per ton hoisted.</summary>
        public const float RopeWearPerTon = 0.002f;

        /// <summary>Records hoisting work — wears the rope by tons hoisted.</summary>
        public string RecordHoistingWork(int tonsHoisted, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!CanHoist)
                return $"MineHoist.RecordHoistingWork: hoist '{hoistId}' cannot hoist (condition {Condition01:P0}, rope {RopeCondition01:P0}) — repair or replace rope first.";
            if (tonsHoisted <= 0)
                return "MineHoist.RecordHoistingWork: tons hoisted must be positive.";

            ropeCondition01 = Mathf.Clamp01(ropeCondition01 - tonsHoisted * RopeWearPerTon);
            if (RopeNeedsReplacement)
                diagnostics.Add($"MineHoist.RecordHoistingWork: hoist '{hoistId}' rope needs replacement (condition {RopeCondition01:P0}).");
            return null;
        }

        public void Inspect(int dayIndex)
        {
            lastInspectionDayIndex = Math.Max(0, dayIndex);
        }

        /// <summary>Replaces the rope/cable. The cost is recorded by the caller (real money).</summary>
        public string ReplaceRope(int dayIndex)
        {
            ropeCondition01 = 1f;
            lastInspectionDayIndex = Math.Max(0, dayIndex);
            return null;
        }

        /// <summary>Wears the general plant condition (weather, use).</summary>
        public void RecordWear(float amount)
        {
            condition01 = Mathf.Clamp01(condition01 - Math.Max(0f, amount));
        }

        /// <summary>Repairs the plant back to serviceable condition. Cost recorded by the caller.</summary>
        public void Repair(int dayIndex)
        {
            condition01 = 1f;
            lastInspectionDayIndex = Math.Max(0, dayIndex);
            if (!inService)
                inService = true;
        }

        public void SetOutOfService()
        {
            inService = false;
        }

        public MineHoistSaveDto CaptureSaveDto()
        {
            return new MineHoistSaveDto
            {
                hoistId = HoistId,
                hoistKind = hoistKind,
                shaftId = ShaftId,
                installedDayIndex = InstalledDayIndex,
                condition01 = Condition01,
                ropeCondition01 = RopeCondition01,
                lastInspectionDayIndex = lastInspectionDayIndex,
                inService = inService,
            };
        }

        public static MineHoist FromSaveDto(MineHoistSaveDto dto)
        {
            if (dto == null)
                return null;
            return new MineHoist(dto.hoistId ?? string.Empty, dto.hoistKind, dto.shaftId ?? string.Empty, dto.installedDayIndex)
            {
                condition01 = Mathf.Clamp01(dto.condition01),
                ropeCondition01 = Mathf.Clamp01(dto.ropeCondition01),
                lastInspectionDayIndex = dto.lastInspectionDayIndex,
                inService = dto.inService,
            };
        }
    }

    /// <summary>
    /// W8D: the mine's installed hoisting plants. One hoist per shaft at
    /// most (a second install on the same shaft is refused — the shaft
    /// already has hoisting).
    /// </summary>
    [Serializable]
    public sealed class MineHoistRegister
    {
        [SerializeField]
        private List<MineHoist> hoists = new List<MineHoist>();

        [SerializeField, Min(0)]
        private int nextHoistNumber = 1;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<MineHoist> Hoists => hoists;

        public MineHoistRegister() { }

        public MineHoist InstallHoist(HoistKind kind, string shaftId, int dayIndex, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (kind == HoistKind.Unspecified)
            {
                callerDiagnostics.Add("MineHoistRegister.InstallHoist: a real hoist kind is required.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(shaftId))
            {
                callerDiagnostics.Add("MineHoistRegister.InstallHoist: shaft is required.");
                return null;
            }
            foreach (MineHoist existing in hoists)
            {
                if (string.Equals(existing.ShaftId, shaftId, StringComparison.Ordinal) && existing.InService)
                {
                    callerDiagnostics.Add($"MineHoistRegister.InstallHoist: shaft '{shaftId}' already has a hoisting plant.");
                    return null;
                }
            }
            var hoist = new MineHoist($"hoist-{nextHoistNumber++}", kind, shaftId, dayIndex);
            hoists.Add(hoist);
            return hoist;
        }

        public MineHoist FindHoist(string hoistId)
        {
            foreach (MineHoist hoist in hoists)
            {
                if (string.Equals(hoist.HoistId, hoistId, StringComparison.Ordinal))
                    return hoist;
            }
            return null;
        }

        public MineHoist HoistOnShaft(string shaftId)
        {
            foreach (MineHoist hoist in hoists)
            {
                if (string.Equals(hoist.ShaftId, shaftId, StringComparison.Ordinal))
                    return hoist;
            }
            return null;
        }

        public MineHoistRegisterSaveDto CaptureSaveDto()
        {
            var dto = new MineHoistRegisterSaveDto { nextHoistNumber = nextHoistNumber };
            foreach (MineHoist hoist in hoists)
                dto.hoists.Add(hoist.CaptureSaveDto());
            return dto;
        }

        public static MineHoistRegister FromSaveDto(MineHoistRegisterSaveDto dto)
        {
            var register = new MineHoistRegister
            {
                nextHoistNumber = Math.Max(1, dto?.nextHoistNumber ?? 1),
            };
            if (dto != null)
            {
                foreach (MineHoistSaveDto hoistDto in dto.hoists)
                {
                    MineHoist hoist = MineHoist.FromSaveDto(hoistDto);
                    if (hoist != null)
                        register.hoists.Add(hoist);
                }
            }
            return register;
        }
    }

    /// <summary>
    /// W8D: hoisting / pumping / ventilation as task-system data.
    /// Ventilation and drainage are covered by the Canon only as capacity
    /// equipment (Canon Part V: "ventilation/pumping" for hard-rock miners)
    /// and an occupation (pump operator: "Pump; motive power; hoses/pipes;
    /// valves") — so they are task gates on real equipment, with no
    /// invented flood/gas mechanics.
    /// </summary>
    public static class MineHoistingTaskCatalog
    {
        public const string HoistOreTaskId = "mine.hoist-ore";
        public const string InspectHoistTaskId = "mine.inspect-hoist";
        public const string DewaterShaftTaskId = "mine.dewater-shaft";
        public const string VentilateHeadingTaskId = "mine.ventilate-heading";

        public const string PumpAssetKind = "pump";
        public const string CapabilityMinePumping = "mine-pumping";
        public const string CapabilityMineVentilation = "mine-ventilation";
        public const string CapabilityMineHoistMaintenance = "mine-hoist-maintenance";

        /// <summary>TUNING (calibration): work-minutes to hoist one ton of ore.</summary>
        public const int HoistMinutesPerTon = 45;

        public static void Register(TaskAuthority authority, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (authority == null)
            {
                diagnostics.Add("MineHoistingTaskCatalog.Register: TaskAuthority is required.");
                return;
            }
            string ignored;

            var hoistOre = new TaskDefinition(HoistOreTaskId, "Hoist ore (per ton)", HoistMinutesPerTon);
            hoistOre.SetRequiredSkill(MineShaftTaskCatalog.MiningSkillId, new[] { MineShaftTaskCatalog.MiningSkillId });
            hoistOre.SetDefaultPriority(TaskPriority.Normal);
            hoistOre.DomainTags.Add("mining");
            hoistOre.DomainTags.Add("hoisting");
            hoistOre.RequiredCapabilityTags.Add(MineShaftTaskCatalog.CapabilityMineHoisting);
            hoistOre.EquipmentClasses.Add(EquipmentRequirementCodes.Kit(MineShaftTaskCatalog.MinerHandKitId));
            hoistOre.EquipmentClasses.Add(EquipmentRequirementCodes.Asset(MineShaftTaskCatalog.HoistAssetKind));
            authority.RegisterDefinition(hoistOre, out ignored);

            var inspect = new TaskDefinition(InspectHoistTaskId, "Inspect hoisting plant", 120);
            inspect.SetRequiredSkill(MineShaftTaskCatalog.MiningSkillId, new[] { MineShaftTaskCatalog.MiningSkillId });
            inspect.SetDefaultPriority(TaskPriority.High);
            inspect.DomainTags.Add("mining");
            inspect.DomainTags.Add("hoist-maintenance");
            inspect.RequiredCapabilityTags.Add(CapabilityMineHoistMaintenance);
            inspect.EquipmentClasses.Add(EquipmentRequirementCodes.Asset(MineShaftTaskCatalog.HoistAssetKind));
            authority.RegisterDefinition(inspect, out ignored);

            var dewater = new TaskDefinition(DewaterShaftTaskId, "Dewater shaft (per shift)", 480);
            dewater.SetRequiredSkill(MineShaftTaskCatalog.MiningSkillId, new[] { MineShaftTaskCatalog.MiningSkillId });
            dewater.SetDefaultPriority(TaskPriority.Normal);
            dewater.DomainTags.Add("mining");
            dewater.DomainTags.Add("drainage");
            dewater.RequiredCapabilityTags.Add(CapabilityMinePumping);
            dewater.EquipmentClasses.Add(EquipmentRequirementCodes.Asset(PumpAssetKind));
            authority.RegisterDefinition(dewater, out ignored);

            var ventilate = new TaskDefinition(VentilateHeadingTaskId, "Ventilate heading (per shift)", 240);
            ventilate.SetRequiredSkill(MineShaftTaskCatalog.MiningSkillId, new[] { MineShaftTaskCatalog.MiningSkillId });
            ventilate.SetDefaultPriority(TaskPriority.Normal);
            ventilate.DomainTags.Add("mining");
            ventilate.DomainTags.Add("ventilation");
            ventilate.RequiredCapabilityTags.Add(CapabilityMineVentilation);
            ventilate.EquipmentClasses.Add(EquipmentRequirementCodes.Kit(MineShaftTaskCatalog.MinerHandKitId));
            authority.RegisterDefinition(ventilate, out ignored);
        }
    }

    /// <summary>W8D: save DTOs for hoisting plants. Owned by the mine runtime (standing rule).</summary>
    [Serializable]
    public sealed class MineHoistSaveDto
    {
        public string hoistId = string.Empty;
        public HoistKind hoistKind = HoistKind.Unspecified;
        public string shaftId = string.Empty;
        public int installedDayIndex;
        public float condition01 = 1f;
        public float ropeCondition01 = 1f;
        public int lastInspectionDayIndex = -1;
        public bool inService = true;
    }

    [Serializable]
    public sealed class MineHoistRegisterSaveDto
    {
        public int nextHoistNumber = 1;
        public List<MineHoistSaveDto> hoists = new List<MineHoistSaveDto>();
    }
}
