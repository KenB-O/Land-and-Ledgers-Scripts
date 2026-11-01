using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Tasks;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>Lifecycle of one mine shaft as a property improvement.</summary>
    public enum MineShaftStatus
    {
        Unspecified = 0,
        Planned = 1,      // surveyed and approved, not started
        Sinking = 2,      // shaft sinking in progress
        Timbered = 3,     // sunk to target, timbered, not yet equipped
        HoistingEquipped = 4, // hoisting gear installed, ready for production
        InProduction = 5, // actively worked
        Suspended = 6,    // work paused, kept on the books
        Abandoned = 7,    // given up; improvement value written down
    }

    /// <summary>
    /// W8A: one named shaft as a property improvement. Shafts carry their own
    /// sunk depth, timbered depth, hoist readiness, and capitalized cost — the
    /// cost ledger feed (capitalizedImprovementCents on the plot) reads
    /// CapitalizedCostCents. No synthetic depth: progress is recorded only
    /// through completed sinking/timbering work.
    /// </summary>
    [Serializable]
    public sealed class MineShaft
    {
        [SerializeField]
        private string shaftId = string.Empty;

        [SerializeField]
        private string shaftName = string.Empty;

        [SerializeField]
        private MineralResourceKind mineralKind;

        [SerializeField, TextArea(1, 2)]
        private string collarNote = string.Empty;

        [SerializeField, Min(0)]
        private int sunkDepthFeet;

        [SerializeField, Min(1)]
        private int targetDepthFeet = 50;

        [SerializeField, Min(0)]
        private int timberedDepthFeet;

        [SerializeField]
        private MineShaftStatus status = MineShaftStatus.Planned;

        [SerializeField, Min(0)]
        private int capitalizedCostCents;

        [SerializeField, Min(0)]
        private int lastWorkDayIndex = -1;

        public string ShaftId => shaftId ?? string.Empty;
        public string ShaftName => shaftName ?? string.Empty;
        public MineralResourceKind MineralKind => mineralKind;
        public string CollarNote => collarNote ?? string.Empty;
        public int SunkDepthFeet => Math.Max(0, sunkDepthFeet);
        public int TargetDepthFeet => Math.Max(1, targetDepthFeet);
        public int TimberedDepthFeet => Math.Max(0, Math.Min(timberedDepthFeet, SunkDepthFeet));
        public MineShaftStatus Status => status;
        public int CapitalizedCostCents => Math.Max(0, capitalizedCostCents);
        public int LastWorkDayIndex => lastWorkDayIndex;
        public bool HasHoist => status is MineShaftStatus.HoistingEquipped or MineShaftStatus.InProduction;

        public MineShaft() { }

        public MineShaft(string shaftId, string shaftName, MineralResourceKind mineralKind, int targetDepthFeet)
        {
            this.shaftId = shaftId ?? string.Empty;
            this.shaftName = shaftName ?? string.Empty;
            this.mineralKind = mineralKind;
            this.targetDepthFeet = Math.Max(1, targetDepthFeet);
        }

        /// <summary>
        /// Records completed sinking work. Returns a rejection string (never
        /// throws) when the record is inconsistent: feet must be positive,
        /// timbered depth may never exceed sunk depth, and no work may be
        /// recorded on an abandoned shaft. Sinking cost is capitalized.
        /// </summary>
        public string RecordSinking(int feetSunk, int timberFeet, int costCents, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (status == MineShaftStatus.Abandoned)
                return "MineShaft.RecordSinking: shaft is abandoned; no further work may be recorded.";
            if (feetSunk <= 0)
                return "MineShaft.RecordSinking: sunk feet must be positive.";
            if (timberFeet < 0)
                return "MineShaft.RecordSinking: timbered feet cannot be negative.";
            if (timberFeet > sunkDepthFeet + feetSunk)
            {
                diagnostics.Add($"MineShaft.RecordSinking: timbered length {timberFeet} ft exceeds sunk depth {sunkDepthFeet + feetSunk} ft — capped at sunk depth.");
                timberFeet = sunkDepthFeet + feetSunk;
            }

            int before = sunkDepthFeet;
            sunkDepthFeet = Math.Min(TargetDepthFeet, sunkDepthFeet + feetSunk);
            timberedDepthFeet = Math.Min(sunkDepthFeet, Math.Max(timberedDepthFeet, timberFeet));
            capitalizedCostCents = Math.Max(0, capitalizedCostCents + Math.Max(0, costCents));
            lastWorkDayIndex = dayIndex;

            if (before < TargetDepthFeet && sunkDepthFeet >= TargetDepthFeet)
                status = status == MineShaftStatus.Sinking || status == MineShaftStatus.Planned
                    ? MineShaftStatus.Timbered
                    : status;
            else if (status == MineShaftStatus.Planned)
                status = MineShaftStatus.Sinking;

            return null;
        }

        public string MarkHoistingEquipped(int dayIndex)
        {
            if (status == MineShaftStatus.Abandoned)
                return "MineShaft.MarkHoistingEquipped: shaft is abandoned.";
            if (sunkDepthFeet < TargetDepthFeet)
                return "MineShaft.MarkHoistingEquipped: shaft has not reached target depth.";
            status = MineShaftStatus.HoistingEquipped;
            lastWorkDayIndex = dayIndex;
            return null;
        }

        public string SetInProduction(int dayIndex)
        {
            if (!HasHoist)
                return "MineShaft.SetInProduction: hoisting equipment must be installed first.";
            status = MineShaftStatus.InProduction;
            lastWorkDayIndex = dayIndex;
            return null;
        }

        public string Suspend()
        {
            if (status == MineShaftStatus.Abandoned)
                return "MineShaft.Suspend: already abandoned.";
            status = MineShaftStatus.Suspended;
            return null;
        }

        public string Abandon()
        {
            status = MineShaftStatus.Abandoned;
            return null;
        }

        public MineShaftSaveDto CaptureSaveDto()
        {
            return new MineShaftSaveDto
            {
                shaftId = ShaftId,
                shaftName = ShaftName,
                mineralKind = MineralKind,
                collarNote = CollarNote,
                sunkDepthFeet = SunkDepthFeet,
                targetDepthFeet = TargetDepthFeet,
                timberedDepthFeet = TimberedDepthFeet,
                status = Status,
                capitalizedCostCents = CapitalizedCostCents,
                lastWorkDayIndex = LastWorkDayIndex,
            };
        }

        public static MineShaft FromSaveDto(MineShaftSaveDto dto)
        {
            var shaft = new MineShaft(
                dto?.shaftId ?? string.Empty,
                dto?.shaftName ?? string.Empty,
                dto?.mineralKind ?? MineralResourceKind.Coal,
                dto?.targetDepthFeet ?? 50)
            {
                sunkDepthFeet = Math.Max(0, dto?.sunkDepthFeet ?? 0),
                timberedDepthFeet = Math.Max(0, dto?.timberedDepthFeet ?? 0),
                status = dto?.status ?? MineShaftStatus.Planned,
                capitalizedCostCents = Math.Max(0, dto?.capitalizedCostCents ?? 0),
                lastWorkDayIndex = dto?.lastWorkDayIndex ?? -1,
            };
            shaft.collarNote = dto?.collarNote ?? string.Empty;
            return shaft;
        }
    }

    /// <summary>
    /// W8A: one named mineral vein declared on the mine plan. Veins are
    /// authored declarations (prospector knowledge), not resource oracles —
    /// a declared vein names where the plan expects ore; actual grade is
    /// assayed (W8B). Canon: "Abandoned mining is evidence, not a binary state."
    /// </summary>
    [Serializable]
    public sealed class MineVein
    {
        [SerializeField]
        private string veinId = string.Empty;

        [SerializeField]
        private string veinName = string.Empty;

        [SerializeField]
        private MineralResourceKind mineralKind;

        [SerializeField, TextArea(1, 2)]
        private string strikeNote = string.Empty;

        [SerializeField, TextArea(1, 2)]
        private string dipNote = string.Empty;

        public string VeinId => veinId ?? string.Empty;
        public string VeinName => veinName ?? string.Empty;
        public MineralResourceKind MineralKind => mineralKind;
        public string StrikeNote => strikeNote ?? string.Empty;
        public string DipNote => dipNote ?? string.Empty;

        public MineVein() { }

        public MineVein(string veinId, string veinName, MineralResourceKind mineralKind)
        {
            this.veinId = veinId ?? string.Empty;
            this.veinName = veinName ?? string.Empty;
            this.mineralKind = mineralKind;
        }

        public MineVeinSaveDto CaptureSaveDto()
        {
            return new MineVeinSaveDto
            {
                veinId = VeinId,
                veinName = VeinName,
                mineralKind = MineralKind,
                strikeNote = StrikeNote,
                dipNote = DipNote,
            };
        }

        public static MineVein FromSaveDto(MineVeinSaveDto dto)
        {
            var vein = new MineVein(dto?.veinId ?? string.Empty, dto?.veinName ?? string.Empty,
                dto?.mineralKind ?? MineralResourceKind.Coal);
            vein.strikeNote = dto?.strikeNote ?? string.Empty;
            vein.dipNote = dto?.dipNote ?? string.Empty;
            return vein;
        }
    }

    /// <summary>Lifecycle of one tracked working (level, drift, stope).</summary>
    public enum MineWorkingStatus
    {
        Unspecified = 0,
        Driving = 1,   // being driven/extended
        InOre = 2,     // on the vein, worked for production
        Idle = 3,      // driven but not currently worked
        Caved = 4,     // lost; recorded, not worked
    }

    /// <summary>
    /// W8A: one tracked level/drive off a shaft. Drives record feet driven
    /// and which named vein they follow; production ore (W8B) cites its
    /// level for provenance.
    /// </summary>
    [Serializable]
    public sealed class MineLevel
    {
        [SerializeField]
        private string levelId = string.Empty;

        [SerializeField]
        private string shaftId = string.Empty;

        [SerializeField, Min(1)]
        private int levelNumber = 1;

        [SerializeField, Min(0)]
        private int depthFeet;

        [SerializeField, Min(0)]
        private int driveFeet;

        [SerializeField]
        private string onVeinId = string.Empty;

        [SerializeField]
        private MineWorkingStatus workingStatus = MineWorkingStatus.Driving;

        public string LevelId => levelId ?? string.Empty;
        public string ShaftId => shaftId ?? string.Empty;
        public int LevelNumber => Math.Max(1, levelNumber);
        public int DepthFeet => Math.Max(0, depthFeet);
        public int DriveFeet => Math.Max(0, driveFeet);
        public string OnVeinId => onVeinId ?? string.Empty;
        public MineWorkingStatus WorkingStatus => workingStatus;

        public MineLevel() { }

        public MineLevel(string levelId, string shaftId, int levelNumber, int depthFeet, string onVeinId)
        {
            this.levelId = levelId ?? string.Empty;
            this.shaftId = shaftId ?? string.Empty;
            this.levelNumber = Math.Max(1, levelNumber);
            this.depthFeet = Math.Max(0, depthFeet);
            this.onVeinId = onVeinId ?? string.Empty;
        }

        /// <summary>Records driven feet of level/drift. Refuses non-positive footage.</summary>
        public string RecordDrive(int feet, List<string> diagnostics)
        {
            if (feet <= 0)
                return "MineLevel.RecordDrive: driven feet must be positive.";
            if (workingStatus == MineWorkingStatus.Caved)
                return "MineLevel.RecordDrive: working has caved; drive work cannot continue.";
            driveFeet = Math.Max(0, driveFeet + feet);
            return null;
        }

        public void SetWorkingStatus(MineWorkingStatus status)
        {
            workingStatus = status;
        }

        public MineLevelSaveDto CaptureSaveDto()
        {
            return new MineLevelSaveDto
            {
                levelId = LevelId,
                shaftId = ShaftId,
                levelNumber = LevelNumber,
                depthFeet = DepthFeet,
                driveFeet = DriveFeet,
                onVeinId = OnVeinId,
                workingStatus = WorkingStatus,
            };
        }

        public static MineLevel FromSaveDto(MineLevelSaveDto dto)
        {
            var level = new MineLevel(dto?.levelId ?? string.Empty, dto?.shaftId ?? string.Empty,
                dto?.levelNumber ?? 1, dto?.depthFeet ?? 0, dto?.onVeinId ?? string.Empty)
            {
                driveFeet = Math.Max(0, dto?.driveFeet ?? 0),
                workingStatus = dto?.workingStatus ?? MineWorkingStatus.Driving,
            };
            return level;
        }
    }

    /// <summary>
    /// W8A: the mine plan — shafts as property improvements, levels/drives as
    /// tracked workings, named veins as the authored mine plan. The plan is the
    /// authority for what workings exist; actual depths/feeds are recorded
    /// only from completed work (no synthetic progress).
    /// </summary>
    [Serializable]
    public sealed class MineShaftPlan
    {
        [SerializeField]
        private List<MineShaft> shafts = new List<MineShaft>();

        [SerializeField]
        private List<MineLevel> levels = new List<MineLevel>();

        [SerializeField]
        private List<MineVein> veins = new List<MineVein>();

        [SerializeField, Min(0)]
        private int nextShaftNumber = 1;

        [SerializeField, Min(0)]
        private int nextLevelNumber = 1;

        [SerializeField, Min(0)]
        private int nextVeinNumber = 1;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<MineShaft> Shafts => shafts;
        public IReadOnlyList<MineLevel> Levels => levels;
        public IReadOnlyList<MineVein> Veins => veins;

        public MineShaftPlan() { }

        /// <summary>Plans a new shaft. Shaft names are unique per mine.</summary>
        public MineShaft PlanShaft(string shaftName, MineralResourceKind mineralKind, int targetDepthFeet, string collarNote)
        {
            if (string.IsNullOrWhiteSpace(shaftName))
            {
                diagnostics.Add("MineShaftPlan.PlanShaft: shaft name is required.");
                return null;
            }
            foreach (MineShaft existing in shafts)
            {
                if (string.Equals(existing.ShaftName, shaftName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add($"MineShaftPlan.PlanShaft: shaft '{shaftName.Trim()}' is already on the plan.");
                    return null;
                }
            }
            var shaft = new MineShaft($"shaft-{nextShaftNumber++}", shaftName.Trim(), mineralKind,
                Math.Max(1, targetDepthFeet));
            shafts.Add(shaft);
            return shaft;
        }

        public MineShaft FindShaft(string shaftId)
        {
            foreach (MineShaft shaft in shafts)
            {
                if (string.Equals(shaft.ShaftId, shaftId, StringComparison.Ordinal))
                    return shaft;
            }
            return null;
        }

        /// <summary>Declares a named vein on the mine plan. Vein names are unique per mine.</summary>
        public MineVein DeclareVein(string veinName, MineralResourceKind mineralKind, string strikeNote, string dipNote)
        {
            if (string.IsNullOrWhiteSpace(veinName))
            {
                diagnostics.Add("MineShaftPlan.DeclareVein: vein name is required.");
                return null;
            }
            foreach (MineVein existing in veins)
            {
                if (string.Equals(existing.VeinName, veinName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add($"MineShaftPlan.DeclareVein: vein '{veinName.Trim()}' is already declared.");
                    return null;
                }
            }
            var vein = new MineVein($"vein-{nextVeinNumber++}", veinName.Trim(), mineralKind);
            veins.Add(vein);
            return vein;
        }

        public MineVein FindVein(string veinId)
        {
            foreach (MineVein vein in veins)
            {
                if (string.Equals(vein.VeinId, veinId, StringComparison.Ordinal))
                    return vein;
            }
            return null;
        }

        /// <summary>
        /// Plans a level off a shaft. The level depth may not exceed the
        /// shaft's target depth; a vein reference must name a declared vein
        /// (or be empty for a crosscut/search drive).
        /// </summary>
        public MineLevel PlanLevel(string shaftId, int levelNumber, int depthFeet, string onVeinId)
        {
            MineShaft shaft = FindShaft(shaftId);
            if (shaft == null)
            {
                diagnostics.Add($"MineShaftPlan.PlanLevel: unknown shaft '{shaftId}'.");
                return null;
            }
            if (depthFeet > shaft.TargetDepthFeet)
            {
                diagnostics.Add($"MineShaftPlan.PlanLevel: level depth {depthFeet} ft exceeds shaft target {shaft.TargetDepthFeet} ft.");
                return null;
            }
            if (!string.IsNullOrWhiteSpace(onVeinId) && FindVein(onVeinId) == null)
            {
                diagnostics.Add($"MineShaftPlan.PlanLevel: vein '{onVeinId}' is not declared on this mine plan.");
                return null;
            }
            var level = new MineLevel($"level-{nextLevelNumber++}", shaftId, levelNumber, depthFeet, onVeinId ?? string.Empty);
            levels.Add(level);
            return level;
        }

        public MineLevel FindLevel(string levelId)
        {
            foreach (MineLevel level in levels)
            {
                if (string.Equals(level.LevelId, levelId, StringComparison.Ordinal))
                    return level;
            }
            return null;
        }

        /// <summary>Levels off a shaft that are worked for production (ore provenance cites these).</summary>
        public List<MineLevel> LevelsOnShaft(string shaftId)
        {
            var result = new List<MineLevel>();
            foreach (MineLevel level in levels)
            {
                if (string.Equals(level.ShaftId, shaftId, StringComparison.Ordinal))
                    result.Add(level);
            }
            return result;
        }

        /// <summary>Total capitalized improvement cost across all shafts (feeds the plot improvement ledger).</summary>
        public int TotalCapitalizedCostCents
        {
            get
            {
                int total = 0;
                foreach (MineShaft shaft in shafts)
                    total = Math.Max(0, total + shaft.CapitalizedCostCents);
                return total;
            }
        }

        public MineShaftPlanSaveDto CaptureSaveDto()
        {
            var dto = new MineShaftPlanSaveDto
            {
                nextShaftNumber = nextShaftNumber,
                nextLevelNumber = nextLevelNumber,
                nextVeinNumber = nextVeinNumber,
            };
            foreach (MineShaft shaft in shafts)
                dto.shafts.Add(shaft.CaptureSaveDto());
            foreach (MineLevel level in levels)
                dto.levels.Add(level.CaptureSaveDto());
            foreach (MineVein vein in veins)
                dto.veins.Add(vein.CaptureSaveDto());
            return dto;
        }

        public static MineShaftPlan FromSaveDto(MineShaftPlanSaveDto dto)
        {
            var plan = new MineShaftPlan
            {
                nextShaftNumber = Math.Max(1, dto?.nextShaftNumber ?? 1),
                nextLevelNumber = Math.Max(1, dto?.nextLevelNumber ?? 1),
                nextVeinNumber = Math.Max(1, dto?.nextVeinNumber ?? 1),
            };
            if (dto != null)
            {
                foreach (MineShaftSaveDto shaftDto in dto.shafts)
                    plan.shafts.Add(MineShaft.FromSaveDto(shaftDto));
                foreach (MineLevelSaveDto levelDto in dto.levels)
                    plan.levels.Add(MineLevel.FromSaveDto(levelDto));
                foreach (MineVeinSaveDto veinDto in dto.veins)
                    plan.veins.Add(MineVein.FromSaveDto(veinDto));
            }
            return plan;
        }
    }

    /// <summary>
    /// W8A: shaft/level work as task-system data (BakeryBreadCatalog pattern).
    /// Sinking shafts is labor + timber + equipment: every sinking task
    /// declares timber-set inputs (resolved to real lumber lots by the W8D
    /// timbering demand link) and requires a miner's hand kit; sinking below
    /// the windlass stage requires hoisting equipment (W8D asset).
    /// </summary>
    public static class MineShaftTaskCatalog
    {
        public const string SinkShaftTaskId = "mine.sink-shaft";
        public const string TimberShaftTaskId = "mine.timber-shaft";
        public const string DriveLevelTaskId = "mine.drive-level";
        public const string EquipHoistTaskId = "mine.equip-hoist";

        /// <summary>W8A: item id sinking/timbering/driving tasks declare as input. Resolved to real lumber lots (W4 sawmill/lumberyard provenance).</summary>
        public const string TimberSetItemId = "mine-timber-sets";

        public const string MiningSkillId = "mining";
        public const string MinerHandKitId = "miner-hand-kit"; // ToolKitCatalog.MinerHandKit
        public const string WindlassAssetKind = "windlass";

        /// <summary>W8A: hoisting equipment asset kind (W8D owns the hoist model; the kind string is declared here so sinking-stage tasks can reference it).</summary>
        public const string HoistAssetKind = "hoist";

        public const string CapabilityMineSinking = "mine-sinking";
        public const string CapabilityMineTimbering = "mine-timbering";
        public const string CapabilityMineDriving = "mine-driving";
        public const string CapabilityMineHoisting = "mine-hoisting";

        /// <summary>TUNING (calibration): work-minutes per foot of shaft sunk.</summary>
        public const int SinkMinutesPerFoot = 480;

        /// <summary>TUNING (calibration): work-minutes per foot of shaft timbered.</summary>
        public const int TimberMinutesPerFoot = 240;

        /// <summary>TUNING (calibration): work-minutes per foot of level/drive driven.</summary>
        public const int DriveMinutesPerFoot = 600;

        /// <summary>TUNING (calibration): timber sets consumed per foot of shaft sunk or timbered.</summary>
        public const int TimberSetsPerSunkFoot = 2;

        /// <summary>TUNING (calibration): timber sets consumed per foot of level/drive driven.</summary>
        public const int TimberSetsPerDrivenFoot = 3;

        public static void Register(TaskAuthority authority, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (authority == null)
            {
                diagnostics.Add("MineShaftTaskCatalog.Register: TaskAuthority is required.");
                return;
            }
            string ignored;

            var sink = new TaskDefinition(SinkShaftTaskId, "Sink mine shaft (per foot)", SinkMinutesPerFoot);
            sink.SetRequiredSkill(MiningSkillId, new[] { MiningSkillId });
            sink.SetDefaultPriority(TaskPriority.High);
            sink.DomainTags.Add("mining");
            sink.DomainTags.Add("shaft-sinking");
            sink.RequiredCapabilityTags.Add(CapabilityMineSinking);
            sink.EquipmentClasses.Add(EquipmentRequirementCodes.Kit(MinerHandKitId));
            sink.EquipmentClasses.Add(EquipmentRequirementCodes.Asset(WindlassAssetKind));
            sink.Inputs.Add(new TaskMaterial(TimberSetItemId, TimberSetsPerSunkFoot));
            authority.RegisterDefinition(sink, out ignored);

            var timber = new TaskDefinition(TimberShaftTaskId, "Timber mine shaft (per foot)", TimberMinutesPerFoot);
            timber.SetRequiredSkill(MiningSkillId, new[] { MiningSkillId });
            timber.SetDefaultPriority(TaskPriority.High);
            timber.DomainTags.Add("mining");
            timber.DomainTags.Add("shaft-timbering");
            timber.RequiredCapabilityTags.Add(CapabilityMineTimbering);
            timber.EquipmentClasses.Add(EquipmentRequirementCodes.Kit(MinerHandKitId));
            timber.Inputs.Add(new TaskMaterial(TimberSetItemId, TimberSetsPerSunkFoot));
            authority.RegisterDefinition(timber, out ignored);

            var drive = new TaskDefinition(DriveLevelTaskId, "Drive mine level/drift (per foot)", DriveMinutesPerFoot);
            drive.SetRequiredSkill(MiningSkillId, new[] { MiningSkillId });
            drive.SetDefaultPriority(TaskPriority.Normal);
            drive.DomainTags.Add("mining");
            drive.DomainTags.Add("level-driving");
            drive.RequiredCapabilityTags.Add(CapabilityMineDriving);
            drive.EquipmentClasses.Add(EquipmentRequirementCodes.Kit(MinerHandKitId));
            drive.Inputs.Add(new TaskMaterial(TimberSetItemId, TimberSetsPerDrivenFoot));
            authority.RegisterDefinition(drive, out ignored);

            var equip = new TaskDefinition(EquipHoistTaskId, "Install hoisting equipment on shaft", 1440);
            equip.SetRequiredSkill(MiningSkillId, new[] { MiningSkillId });
            equip.SetDefaultPriority(TaskPriority.High);
            equip.DomainTags.Add("mining");
            equip.DomainTags.Add("hoist-installation");
            equip.RequiredCapabilityTags.Add(CapabilityMineHoisting);
            equip.EquipmentClasses.Add(EquipmentRequirementCodes.Kit(MinerHandKitId));
            equip.EquipmentClasses.Add(EquipmentRequirementCodes.Asset(HoistAssetKind));
            authority.RegisterDefinition(equip, out ignored);
        }
    }

    /// <summary>W8A: save DTOs for the shaft plan. Owned by the mine runtime (standing rule: save-DTO methods live in the owning runtime class).</summary>
    [Serializable]
    public sealed class MineShaftSaveDto
    {
        public string shaftId = string.Empty;
        public string shaftName = string.Empty;
        public MineralResourceKind mineralKind;
        public string collarNote = string.Empty;
        public int sunkDepthFeet;
        public int targetDepthFeet = 50;
        public int timberedDepthFeet;
        public MineShaftStatus status = MineShaftStatus.Planned;
        public int capitalizedCostCents;
        public int lastWorkDayIndex = -1;
    }

    [Serializable]
    public sealed class MineLevelSaveDto
    {
        public string levelId = string.Empty;
        public string shaftId = string.Empty;
        public int levelNumber = 1;
        public int depthFeet;
        public int driveFeet;
        public string onVeinId = string.Empty;
        public MineWorkingStatus workingStatus = MineWorkingStatus.Driving;
    }

    [Serializable]
    public sealed class MineVeinSaveDto
    {
        public string veinId = string.Empty;
        public string veinName = string.Empty;
        public MineralResourceKind mineralKind;
        public string strikeNote = string.Empty;
        public string dipNote = string.Empty;
    }

    [Serializable]
    public sealed class MineShaftPlanSaveDto
    {
        public int nextShaftNumber = 1;
        public int nextLevelNumber = 1;
        public int nextVeinNumber = 1;
        public List<MineShaftSaveDto> shafts = new List<MineShaftSaveDto>();
        public List<MineLevelSaveDto> levels = new List<MineLevelSaveDto>();
        public List<MineVeinSaveDto> veins = new List<MineVeinSaveDto>();
    }
}
