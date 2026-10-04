using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Tailor
{
    /// <summary>
    /// W1C: the tailor's piece-rate schedule. Canon Part V tailor profile: "Turns
    /// cloth and notions into workwear, mending, and clothing service." The tailor
    /// is paid PER GARMENT (piece rates), not per hour — exact rate levels are
    /// calibration (Canon Part XV holds). Money still moves only through ledger
    /// authorities, never as a task side effect; this schedule is DATA the shop
    /// runtime and invoicing paths read.
    /// </summary>
    [Serializable]
    public sealed class TailorPieceRateSchedule
    {
        [SerializeField, Min(0)]
        private int shirtRateCents = 150;

        [SerializeField, Min(0)]
        private int trousersRateCents = 300;

        [SerializeField, Min(0)]
        private int coatRateCents = 750;

        [SerializeField, Min(0)]
        private int dressRateCents = 500;

        [SerializeField, Min(0)]
        private int mendRateCents = 50;

        [SerializeField, Min(0)]
        private int fittingFeeCents = 25;

        public TailorPieceRateSchedule() { }

        public TailorPieceRateSchedule(
            int shirtRateCents, int trousersRateCents, int coatRateCents,
            int dressRateCents, int mendRateCents, int fittingFeeCents)
        {
            this.shirtRateCents = Math.Max(0, shirtRateCents);
            this.trousersRateCents = Math.Max(0, trousersRateCents);
            this.coatRateCents = Math.Max(0, coatRateCents);
            this.dressRateCents = Math.Max(0, dressRateCents);
            this.mendRateCents = Math.Max(0, mendRateCents);
            this.fittingFeeCents = Math.Max(0, fittingFeeCents);
        }

        public int ShirtRateCents => Math.Max(0, shirtRateCents);
        public int TrousersRateCents => Math.Max(0, trousersRateCents);
        public int CoatRateCents => Math.Max(0, coatRateCents);
        public int DressRateCents => Math.Max(0, dressRateCents);
        public int MendRateCents => Math.Max(0, mendRateCents);
        public int FittingFeeCents => Math.Max(0, fittingFeeCents);
    }

    /// <summary>
    /// W1C: one garment type as data — cloth and notions per garment, and the
    /// labor minutes of each production stage. All quantities are calibration
    /// (Canon Part XV).
    /// </summary>
    public struct TailorGarmentSpec
    {
        public string GarmentId;
        public string DisplayName;
        public bool IsMending;
        public int ClothYards;
        public int NotionsUnits;
        public int MeasureMinutes;
        public int CutMinutes;
        public int FitBasteMinutes;
        public int SewMinutes;
        public int FitFinalMinutes;
        public int PressMinutes;
        public int AssessMinutes;
        public int MendMinutes;
    }

    /// <summary>
    /// W1C: garment order work as task-system data (TTS-5 pattern, following
    /// BarberServiceCatalog). Canon Part V equipment survey: shears, needles,
    /// thread, measuring tape, chalk, thimble, pins, irons, cutting table,
    /// patterns (core); sewing machine and larger pressing table are scale only.
    /// Canon 4.7: the sewing machine NEVER gates tailoring — hand methods remain
    /// physically possible — so the NX-1 teeth gate is the tailor's hand kit
    /// ("tailor-hand-kit", ToolKitCatalog), and cut/sew/press stages require the
    /// cutting-table workstation (Tech X §3.5: Ironing/CuttingTable). Execution
    /// fees live in <see cref="TailorPieceRateSchedule"/>; task definitions never
    /// carry live prices.
    /// </summary>
    public static class TailorGarmentCatalog
    {
        public const string ShirtId = "tail.shirt";
        public const string TrousersId = "tail.trousers";
        public const string CoatId = "tail.coat";
        public const string DressId = "tail.dress";
        public const string MendId = "tail.mend";

        /// <summary>W1C: item ids garment tasks declare as inputs. Resolved to real lots by the runtime.</summary>
        public const string ClothItemId = "tailor-cloth";

        /// <summary>W1C: item ids garment tasks declare as inputs. Resolved to real lots by the runtime.</summary>
        public const string NotionsItemId = "tailor-notions";

        public const string TailoringSkillId = "tailoring";

        /// <summary>W1C: the workstation id cut/sew/press stages require. Defined in WorkstationCatalog; the shop runtime pools tables and assigns them per stage.</summary>
        public const string TailorCuttingTableStationId = "tailor-cutting-table";

        /// <summary>TUNING: cloth yards per work shirt.</summary>
        public const int ShirtClothYards = 3;

        /// <summary>TUNING: cloth yards per pair of trousers.</summary>
        public const int TrousersClothYards = 2;

        /// <summary>TUNING: cloth yards per sack coat.</summary>
        public const int CoatClothYards = 4;

        /// <summary>TUNING: cloth yards per calico dress.</summary>
        public const int DressClothYards = 6;

        /// <summary>TUNING: notions (thread/buttons) per garment.</summary>
        public const int StandardNotionsUnits = 1;

        /// <summary>TUNING: notions per coat or dress (buttons, extra thread).</summary>
        public const int HeavyNotionsUnits = 2;

        /// <summary>TUNING: stage labor minutes for a work shirt.</summary>
        public const int ShirtMeasureMinutes = 20;
        public const int ShirtCutMinutes = 60;
        public const int ShirtFitMinutes = 20;
        public const int ShirtSewMinutes = 240;
        public const int ShirtPressMinutes = 20;

        /// <summary>TUNING: stage labor minutes for trousers.</summary>
        public const int TrousersMeasureMinutes = 20;
        public const int TrousersCutMinutes = 45;
        public const int TrousersFitMinutes = 20;
        public const int TrousersSewMinutes = 300;
        public const int TrousersPressMinutes = 20;

        /// <summary>TUNING: stage labor minutes for a sack coat.</summary>
        public const int CoatMeasureMinutes = 30;
        public const int CoatCutMinutes = 120;
        public const int CoatFitMinutes = 30;
        public const int CoatSewMinutes = 480;
        public const int CoatPressMinutes = 30;

        /// <summary>TUNING: stage labor minutes for a calico dress.</summary>
        public const int DressMeasureMinutes = 30;
        public const int DressCutMinutes = 90;
        public const int DressFitMinutes = 30;
        public const int DressSewMinutes = 420;
        public const int DressPressMinutes = 30;

        /// <summary>TUNING: mending labor minutes (assess, then mend).</summary>
        public const int MendAssessMinutes = 30;
        public const int MendWorkMinutes = 90;

        /// <summary>Garment specs as data, for the shop runtime's scheduler.</summary>
        public static TailorGarmentSpec GetSpec(string garmentId)
        {
            if (string.Equals(garmentId, ShirtId, StringComparison.Ordinal))
                return new TailorGarmentSpec
                {
                    GarmentId = ShirtId, DisplayName = "Work shirt",
                    ClothYards = ShirtClothYards, NotionsUnits = StandardNotionsUnits,
                    MeasureMinutes = ShirtMeasureMinutes, CutMinutes = ShirtCutMinutes,
                    FitBasteMinutes = ShirtFitMinutes, SewMinutes = ShirtSewMinutes,
                    FitFinalMinutes = ShirtFitMinutes, PressMinutes = ShirtPressMinutes,
                };
            if (string.Equals(garmentId, TrousersId, StringComparison.Ordinal))
                return new TailorGarmentSpec
                {
                    GarmentId = TrousersId, DisplayName = "Trousers",
                    ClothYards = TrousersClothYards, NotionsUnits = StandardNotionsUnits,
                    MeasureMinutes = TrousersMeasureMinutes, CutMinutes = TrousersCutMinutes,
                    FitBasteMinutes = TrousersFitMinutes, SewMinutes = TrousersSewMinutes,
                    FitFinalMinutes = TrousersFitMinutes, PressMinutes = TrousersPressMinutes,
                };
            if (string.Equals(garmentId, CoatId, StringComparison.Ordinal))
                return new TailorGarmentSpec
                {
                    GarmentId = CoatId, DisplayName = "Sack coat",
                    ClothYards = CoatClothYards, NotionsUnits = HeavyNotionsUnits,
                    MeasureMinutes = CoatMeasureMinutes, CutMinutes = CoatCutMinutes,
                    FitBasteMinutes = CoatFitMinutes, SewMinutes = CoatSewMinutes,
                    FitFinalMinutes = CoatFitMinutes, PressMinutes = CoatPressMinutes,
                };
            if (string.Equals(garmentId, DressId, StringComparison.Ordinal))
                return new TailorGarmentSpec
                {
                    GarmentId = DressId, DisplayName = "Calico dress",
                    ClothYards = DressClothYards, NotionsUnits = HeavyNotionsUnits,
                    MeasureMinutes = DressMeasureMinutes, CutMinutes = DressCutMinutes,
                    FitBasteMinutes = DressFitMinutes, SewMinutes = DressSewMinutes,
                    FitFinalMinutes = DressFitMinutes, PressMinutes = DressPressMinutes,
                };
            if (string.Equals(garmentId, MendId, StringComparison.Ordinal))
                return new TailorGarmentSpec
                {
                    GarmentId = MendId, DisplayName = "Mending",
                    IsMending = true, ClothYards = 0, NotionsUnits = StandardNotionsUnits,
                    AssessMinutes = MendAssessMinutes, MendMinutes = MendWorkMinutes,
                };
            return default;
        }

        /// <summary>True for the five offered garment/mending types; false for anything else (unknown ids never schedule).</summary>
        public static bool IsKnownGarment(string garmentId)
        {
            return !string.IsNullOrEmpty(GetSpec(garmentId).GarmentId);
        }

        /// <summary>Reads the piece rate for a garment from the schedule. Unknown garments price at zero, never guessed.</summary>
        public static int GetPieceRateCents(string garmentId, TailorPieceRateSchedule rates)
        {
            rates = rates ?? new TailorPieceRateSchedule();
            if (string.Equals(garmentId, ShirtId, StringComparison.Ordinal)) return rates.ShirtRateCents;
            if (string.Equals(garmentId, TrousersId, StringComparison.Ordinal)) return rates.TrousersRateCents;
            if (string.Equals(garmentId, CoatId, StringComparison.Ordinal)) return rates.CoatRateCents;
            if (string.Equals(garmentId, DressId, StringComparison.Ordinal)) return rates.DressRateCents;
            if (string.Equals(garmentId, MendId, StringComparison.Ordinal)) return rates.MendRateCents;
            return 0;
        }

        /// <summary>Task id for a garment's production stage (e.g. "tail.cut.tail.shirt"). Mending uses the explicit ids below.</summary>
        public static string StageTaskId(string stage, string garmentId)
        {
            return $"tail.{stage}.{garmentId}";
        }

        /// <summary>W1C: explicit task ids for the mending stages (no cloth, no fittings).</summary>
        public const string AssessMendTaskId = "tail.assess-mend";

        /// <summary>W1C: explicit task ids for the mending stages (no cloth, no fittings).</summary>
        public const string MendTaskId = "tail.mend";

        /// <summary>Registers the tailoring skill via the TTS-3 extension path.</summary>
        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("TailorGarmentCatalog: no SkillService — tailoring skill not registered.");
                return;
            }

            if (skillService.GetSkill(TailoringSkillId) != null) return;
            if (!skillService.RegisterSkill(new SkillDefinition(TailoringSkillId, "Tailoring",
                "Bespoke garment work: measuring, cutting, basting, sewing, fitting, pressing, mending. (TTS-3 extension path; Canon Part V tailor.)"),
                out string rejection))
            {
                diagnostics.Add($"TailorGarmentCatalog: skill '{TailoringSkillId}' rejected: {rejection}");
            }
        }

        /// <summary>Registers the garment production task definitions. Safe to call once at boot.</summary>
        public static void RegisterAll(TaskAuthority authority)
        {
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            string ignored;

            // NX-1 teeth gate: every stage requires a usable tailor's hand kit
            // (shears, needles, thread, tape, chalk, thimble, pins, irons, patterns).
            // Canon 4.7: the sewing machine is capacity only and NEVER gates — it is
            // not a requirement here.
            string kit = EquipmentRequirementCodes.Kit("tailor-hand-kit");
            // Table gate as data: cut/sew/press happen at a cutting table station;
            // the shop runtime pools tables and assigns them per stage.
            string table = EquipmentRequirementCodes.Workstation(TailorCuttingTableStationId);

            foreach (string garmentId in new[] { ShirtId, TrousersId, CoatId, DressId })
            {
                TailorGarmentSpec spec = GetSpec(garmentId);
                string suffix = garmentId; // e.g. "tail.shirt"

                RegisterStage(authority, StageTaskId("measure", suffix), $"Tailor measuring ({spec.DisplayName})",
                    spec.MeasureMinutes, kit, null, null, 0, out ignored);
                RegisterStage(authority, StageTaskId("cut", suffix), $"Tailor cutting ({spec.DisplayName})",
                    spec.CutMinutes, kit, table, ClothItemId, spec.ClothYards, out ignored);
                RegisterStage(authority, StageTaskId("fit-baste", suffix), $"Tailor basting fitting ({spec.DisplayName})",
                    spec.FitBasteMinutes, kit, null, null, 0, out ignored, isFitting: true);
                RegisterStage(authority, StageTaskId("sew", suffix), $"Tailor sewing ({spec.DisplayName})",
                    spec.SewMinutes, kit, table, NotionsItemId, spec.NotionsUnits, out ignored);
                RegisterStage(authority, StageTaskId("fit-final", suffix), $"Tailor final fitting ({spec.DisplayName})",
                    spec.FitFinalMinutes, kit, null, null, 0, out ignored, isFitting: true);
                RegisterStage(authority, StageTaskId("press", suffix), $"Tailor pressing ({spec.DisplayName})",
                    spec.PressMinutes, kit, table, null, 0, out ignored);
            }

            // Mending: assess, then mend — notions only, no cloth, no fittings.
            RegisterStage(authority, AssessMendTaskId, "Tailor assessing mending",
                MendAssessMinutes, kit, null, null, 0, out ignored, isRepair: true);
            RegisterStage(authority, MendTaskId, "Tailor mending",
                MendWorkMinutes, kit, table, NotionsItemId, StandardNotionsUnits, out ignored, isRepair: true);
        }

        private static void RegisterStage(
            TaskAuthority authority, string taskId, string displayName, int baseMinutes,
            string kitCode, string tableCode, string inputItemId, int inputQuantity,
            out string ignored, bool isFitting = false, bool isRepair = false)
        {
            var task = new TaskDefinition(taskId, displayName, Math.Max(1, baseMinutes));
            task.SetRequiredSkill(TailoringSkillId, new[] { TailoringSkillId });
            task.SetDefaultPriority(TaskPriority.Normal);
            task.DomainTags.Add("personal-service");
            task.DomainTags.Add("garment");
            if (isFitting) task.DomainTags.Add("fitting");
            if (isRepair) task.DomainTags.Add("repair");
            task.EquipmentClasses.Add(kitCode);
            if (!string.IsNullOrEmpty(tableCode)) task.EquipmentClasses.Add(tableCode);
            if (!string.IsNullOrEmpty(inputItemId) && inputQuantity > 0)
            {
                task.Inputs.Add(new TaskMaterial(inputItemId, inputQuantity));
            }

            authority.RegisterDefinition(task, out ignored);
        }
    }
}
