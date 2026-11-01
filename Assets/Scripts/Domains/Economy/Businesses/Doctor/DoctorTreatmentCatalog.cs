using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>
    /// W1A: the doctor's fee schedule. Canon §13.3C: medical service can be paid
    /// at the visit, carried on an account, paid partly later, or covered by an
    /// employer — exact fee levels are calibration items (Canon Part XV holds).
    /// Money still moves only through ledger authorities, never as a task side
    /// effect; this schedule is DATA the house-call and invoicing paths read.
    /// </summary>
    [Serializable]
    public sealed class DoctorFeeSchedule
    {
        [SerializeField, Min(0)]
        private int officeVisitFeeCents = 100;

        [SerializeField, Min(0)]
        private int houseCallOutFeeCents = 250;

        [SerializeField, Min(0)]
        private int houseCallTreatmentFeeCents = 100;

        [SerializeField, Min(0)]
        private int perMileTravelCents = 10;

        [SerializeField, Min(0)]
        private int remedyDoseChargeCents = 25;

        [SerializeField, Min(0)]
        private int minorProcedureFeeCents = 150;

        public DoctorFeeSchedule() { }

        public DoctorFeeSchedule(
            int officeVisitFeeCents,
            int houseCallOutFeeCents,
            int houseCallTreatmentFeeCents,
            int perMileTravelCents,
            int remedyDoseChargeCents,
            int minorProcedureFeeCents)
        {
            this.officeVisitFeeCents = Math.Max(0, officeVisitFeeCents);
            this.houseCallOutFeeCents = Math.Max(0, houseCallOutFeeCents);
            this.houseCallTreatmentFeeCents = Math.Max(0, houseCallTreatmentFeeCents);
            this.perMileTravelCents = Math.Max(0, perMileTravelCents);
            this.remedyDoseChargeCents = Math.Max(0, remedyDoseChargeCents);
            this.minorProcedureFeeCents = Math.Max(0, minorProcedureFeeCents);
        }

        public int OfficeVisitFeeCents => Math.Max(0, officeVisitFeeCents);
        public int HouseCallOutFeeCents => Math.Max(0, houseCallOutFeeCents);
        public int HouseCallTreatmentFeeCents => Math.Max(0, houseCallTreatmentFeeCents);
        public int PerMileTravelCents => Math.Max(0, perMileTravelCents);
        public int RemedyDoseChargeCents => Math.Max(0, remedyDoseChargeCents);
        public int MinorProcedureFeeCents => Math.Max(0, minorProcedureFeeCents);
    }

    /// <summary>
    /// W1A: treatment work as task-system data (TTS-5 pattern, following
    /// NewspaperTaskCatalog). Canon §13.3A: a doctor sells scarce professional
    /// time — office visits concentrate patients, house calls burn travel time.
    /// All minute values are calibration (Canon Part XV). Execution fees live in
    /// <see cref="DoctorFeeSchedule"/>; task definitions never carry live prices.
    /// </summary>
    public static class DoctorTreatmentCatalog
    {
        public const string OfficeVisitId = "doc.office-visit";
        public const string HouseCallId = "doc.house-call";
        public const string DispenseRemedyId = "doc.dispense-remedy";
        public const string MinorProcedureId = "doc.minor-procedure";

        /// <summary>W1A: item ids treatment tasks declare as inputs. Resolved to real lots by the runtime.</summary>
        public const string MedicineDoseItemId = "medicine-dose";
        public const string DressingItemId = "wound-dressing";

        public const string DoctoringSkillId = "doctoring";

        /// <summary>TUNING: minutes of examination + treatment for one office patient.</summary>
        public const int OfficeVisitMinutes = 30;

        /// <summary>TUNING: minutes of bedside treatment for one house-call patient (travel booked separately via JRN).</summary>
        public const int HouseCallBedsideMinutes = 60;

        /// <summary>TUNING: minutes to dispense a remedy from the medicine cabinet.</summary>
        public const int DispenseRemedyMinutes = 10;

        /// <summary>TUNING: minutes for a minor office procedure (wound dressing, splinting, extraction).</summary>
        public const int MinorProcedureMinutes = 45;

        /// <summary>Registers the doctoring skill via the TTS-3 extension path.</summary>
        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("DoctorTreatmentCatalog: no SkillService — doctoring skill not registered.");
                return;
            }

            if (skillService.GetSkill(DoctoringSkillId) != null) return;
            if (!skillService.RegisterSkill(new SkillDefinition(DoctoringSkillId, "Doctoring",
                "Professional medical care: examination, treatment, wound care, remedy dispensing. (TTS-3 extension path; Canon Part V medical/pharmacy; Canon §13.3.)"),
                out string rejection))
            {
                diagnostics.Add($"DoctorTreatmentCatalog: skill '{DoctoringSkillId}' rejected: {rejection}");
            }
        }

        /// <summary>Registers the four treatment task definitions. Safe to call once at boot.</summary>
        public static void RegisterAll(TaskAuthority authority)
        {
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            string ignored;

            // NX-1 teeth gate: every treatment task requires a usable doctor's bag.
            // Tech X §3.4 ToolKit: DoctorBag ("doctor-bag") already exists in ToolKitCatalog.
            string bag = EquipmentRequirementCodes.Kit("doctor-bag");

            var office = new TaskDefinition(OfficeVisitId, "Doctor office visit", OfficeVisitMinutes);
            office.SetRequiredSkill(DoctoringSkillId, new[] { DoctoringSkillId });
            office.SetDefaultPriority(TaskPriority.Normal);
            office.DomainTags.Add("medicine");
            office.DomainTags.Add("personal-service");
            office.EquipmentClasses.Add(bag);
            authority.RegisterDefinition(office, out ignored);

            var call = new TaskDefinition(HouseCallId, "Doctor house call", HouseCallBedsideMinutes);
            call.SetRequiredSkill(DoctoringSkillId, new[] { DoctoringSkillId });
            call.SetDefaultPriority(TaskPriority.High);
            call.DomainTags.Add("medicine");
            call.DomainTags.Add("personal-service");
            call.DomainTags.Add("travel");
            call.EquipmentClasses.Add(bag);
            authority.RegisterDefinition(call, out ignored);

            var dispense = new TaskDefinition(DispenseRemedyId, "Dispense remedy", DispenseRemedyMinutes);
            dispense.SetRequiredSkill(DoctoringSkillId, new[] { DoctoringSkillId });
            dispense.SetDefaultPriority(TaskPriority.Normal);
            dispense.DomainTags.Add("medicine");
            dispense.EquipmentClasses.Add(bag);
            dispense.Inputs.Add(new TaskMaterial(MedicineDoseItemId, 1));
            authority.RegisterDefinition(dispense, out ignored);

            var procedure = new TaskDefinition(MinorProcedureId, "Minor procedure", MinorProcedureMinutes);
            procedure.SetRequiredSkill(DoctoringSkillId, new[] { DoctoringSkillId });
            procedure.SetDefaultPriority(TaskPriority.High);
            procedure.DomainTags.Add("medicine");
            procedure.DomainTags.Add("personal-service");
            procedure.EquipmentClasses.Add(bag);
            procedure.Inputs.Add(new TaskMaterial(DressingItemId, 1));
            authority.RegisterDefinition(procedure, out ignored);
        }
    }
}
