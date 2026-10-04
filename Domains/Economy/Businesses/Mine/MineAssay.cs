using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// W8B: one completed assay — who assayed which lot, when, by what
    /// method, and the measured grade. The assayer must be a real person
    /// (EntityKind.Person) with the assaying capability; the assay task
    /// requires the assay-bench workstation (Tech X §3.5, Canon Part V
    /// assayer profile: assay furnace, precision balance, crucibles).
    /// </summary>
    [Serializable]
    public sealed class MineAssayResult
    {
        [SerializeField]
        private string assayId = string.Empty;

        [SerializeField]
        private EntityId lotId = EntityId.Invalid;

        [SerializeField]
        private EntityId assayerPersonId = EntityId.Invalid;

        [SerializeField, Min(0)]
        private int assayDayIndex;

        [SerializeField, Min(0)]
        private float assayedGradeValue;

        [SerializeField]
        private string gradeUnit = string.Empty;

        [SerializeField, TextArea(1, 2)]
        private string methodNote = string.Empty;

        public string AssayId => assayId ?? string.Empty;
        public EntityId LotId => lotId;
        public EntityId AssayerPersonId => assayerPersonId;
        public int AssayDayIndex => Math.Max(0, assayDayIndex);
        public float AssayedGradeValue => Math.Max(0f, assayedGradeValue);
        public string GradeUnit => gradeUnit ?? string.Empty;
        public string MethodNote => methodNote ?? string.Empty;

        public MineAssayResult() { }

        public MineAssayResult(string assayId, EntityId lotId, EntityId assayerPersonId, int assayDayIndex,
            float assayedGradeValue, string gradeUnit, string methodNote)
        {
            this.assayId = assayId ?? string.Empty;
            this.lotId = lotId;
            this.assayerPersonId = assayerPersonId;
            this.assayDayIndex = Math.Max(0, assayDayIndex);
            this.assayedGradeValue = Math.Max(0f, assayedGradeValue);
            this.gradeUnit = gradeUnit ?? string.Empty;
            this.methodNote = methodNote ?? string.Empty;
        }

        public MineAssayResultSaveDto CaptureSaveDto()
        {
            return new MineAssayResultSaveDto
            {
                assayId = AssayId,
                lotKind = (int)lotId.Kind,
                lotSeq = lotId.Id,
                assayerKind = (int)assayerPersonId.Kind,
                assayerSeq = assayerPersonId.Id,
                assayDayIndex = AssayDayIndex,
                assayedGradeValue = AssayedGradeValue,
                gradeUnit = GradeUnit,
                methodNote = MethodNote,
            };
        }

        public static MineAssayResult FromSaveDto(MineAssayResultSaveDto dto)
        {
            if (dto == null)
                return null;
            return new MineAssayResult(
                dto.assayId ?? string.Empty,
                new EntityId { Kind = (EntityKind)dto.lotKind, Id = dto.lotSeq },
                new EntityId { Kind = (EntityKind)dto.assayerKind, Id = dto.assayerSeq },
                dto.assayDayIndex, dto.assayedGradeValue, dto.gradeUnit ?? string.Empty, dto.methodNote ?? string.Empty);
        }
    }

    /// <summary>
    /// W8B: assay work as task-system data (BakeryBreadCatalog pattern).
    /// Assaying requires the assay-bench workstation (WorkstationCatalog:
    /// assay furnace, assay balance, crucible set; operator skill
    /// "assaying") — Canon Part V assayer profile.
    /// </summary>
    public static class MineAssayTaskCatalog
    {
        public const string AssaySampleTaskId = "mine.assay-sample";
        public const string AssayingSkillId = "assaying";
        public const string AssayBenchStationId = "assay-bench"; // WorkstationCatalog.AssayBench
        public const string CapabilityMineAssaying = "mine-assaying";

        /// <summary>TUNING (calibration): work-minutes to assay one ore sample.</summary>
        public const int AssayMinutesPerSample = 240;

        public static void Register(TaskAuthority authority, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (authority == null)
            {
                diagnostics.Add("MineAssayTaskCatalog.Register: TaskAuthority is required.");
                return;
            }
            string ignored;

            var assay = new TaskDefinition(AssaySampleTaskId, "Assay ore sample", AssayMinutesPerSample);
            assay.SetRequiredSkill(AssayingSkillId, new[] { AssayingSkillId });
            assay.SetDefaultPriority(TaskPriority.Normal);
            assay.DomainTags.Add("mining");
            assay.DomainTags.Add("assay");
            assay.RequiredCapabilityTags.Add(CapabilityMineAssaying);
            assay.EquipmentClasses.Add(EquipmentRequirementCodes.Workstation(AssayBenchStationId));
            authority.RegisterDefinition(assay, out ignored);
        }
    }

    /// <summary>
    /// W8B: the assay desk — records assay results against ore lots. The
    /// service validates that the lot is on the stockpile, the assayer is a
    /// real person, and the grade is non-negative; violations are refused
    /// loudly, never silently corrected.
    ///
    /// DESIGN FORK (canon-gated, skipped): high-grading / ore theft by miners.
    /// The Canon and historical research name no theft mechanics, quantities,
    /// or detection rules; inventing them would be synthetic. Skipped —
    /// see the W8 package report. Assayed lots are traceable to an assayer
    /// person, which is the honest substrate a future theft mechanic would
    /// build on.
    /// </summary>
    public sealed class MineAssayService
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Assays a lot on the stockpile. Returns the assay result, or null
        /// (with a diagnostic) when validation fails. The result is recorded
        /// on the stockpile's assay list.
        /// </summary>
        public MineAssayResult AssayLot(MineOreStock stock, EntityId lotId, EntityId assayerPersonId,
            int assayDayIndex, float assayedGradeValue, string methodNote)
        {
            if (stock == null)
            {
                diagnostics.Add("MineAssayService.AssayLot: ore stock is required.");
                return null;
            }
            MineOreLot lot = stock.FindLot(lotId);
            if (lot == null)
            {
                diagnostics.Add($"MineAssayService.AssayLot: lot {lotId.Id} is not on the stockpile — cannot assay ore that does not exist.");
                return null;
            }
            if (assayerPersonId.Equals(EntityId.Invalid) || assayerPersonId.Kind != EntityKind.Person)
            {
                diagnostics.Add("MineAssayService.AssayLot: assayer must be a real person — no anonymous assays.");
                return null;
            }
            if (assayedGradeValue < 0f)
            {
                diagnostics.Add("MineAssayService.AssayLot: grade cannot be negative.");
                return null;
            }
            if (lot.Assayed)
            {
                diagnostics.Add($"MineAssayService.AssayLot: lot {lotId.Id} was already assayed ({lot.AssayId}) — re-assay refused; record a new sample instead.");
                return null;
            }

            string assayId = $"assay-{stock.AssayResults.Count + 1}";
            var result = new MineAssayResult(assayId, lotId, assayerPersonId, assayDayIndex,
                assayedGradeValue, lot.GradeUnit, methodNote);
            string rejection = lot.RecordAssay(assayId, assayerPersonId, assayDayIndex, assayedGradeValue);
            if (rejection != null)
            {
                diagnostics.Add($"MineAssayService.AssayLot: {rejection}");
                return null;
            }
            stock.AddAssayResult(result);
            return result;
        }
    }

    /// <summary>W8B: save DTO for assay results. Owned by the mine runtime (standing rule).</summary>
    [Serializable]
    public sealed class MineAssayResultSaveDto
    {
        public string assayId = string.Empty;
        public int lotKind;
        public int lotSeq;
        public int assayerKind;
        public int assayerSeq;
        public int assayDayIndex;
        public float assayedGradeValue;
        public string gradeUnit = string.Empty;
        public string methodNote = string.Empty;
    }
}
