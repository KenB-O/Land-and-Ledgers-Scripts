using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Barber
{
    /// <summary>
    /// W1B: the barber's fee schedule. Canon Part V: the barber "provides
    /// periodic haircut, shave, and bath service without a heavy stock chain" —
    /// the sale is scarce professional time plus consumables (soap, linen).
    /// Exact fee levels are calibration (Canon Part XV holds). Money still moves
    /// only through ledger authorities, never as a task side effect; this
    /// schedule is DATA the shop runtime and invoicing paths read.
    /// </summary>
    [Serializable]
    public sealed class BarberFeeSchedule
    {
        [SerializeField, Min(0)]
        private int shaveFeeCents = 15;

        [SerializeField, Min(0)]
        private int haircutFeeCents = 25;

        [SerializeField, Min(0)]
        private int bathFeeCents = 35;

        public BarberFeeSchedule() { }

        public BarberFeeSchedule(int shaveFeeCents, int haircutFeeCents, int bathFeeCents)
        {
            this.shaveFeeCents = Math.Max(0, shaveFeeCents);
            this.haircutFeeCents = Math.Max(0, haircutFeeCents);
            this.bathFeeCents = Math.Max(0, bathFeeCents);
        }

        public int ShaveFeeCents => Math.Max(0, shaveFeeCents);
        public int HaircutFeeCents => Math.Max(0, haircutFeeCents);
        public int BathFeeCents => Math.Max(0, bathFeeCents);
    }

    /// <summary>
    /// W1B: one barber service as data — chair occupancy, the barber's hands-on
    /// labor, and consumables per service. A bath soaks while the barber works
    /// elsewhere (labor &lt; chair minutes); that is the historical concurrency
    /// chairs exist to capture. All quantities are calibration (Canon Part XV).
    /// </summary>
    public struct BarberServiceSpec
    {
        public string ServiceId;
        public string DisplayName;
        public int ChairMinutes;
        public int LaborMinutes;
        public int SoapUnits;
        public int LinenUnits;
    }

    /// <summary>
    /// W1B: shave / haircut / bath as task-system data (TTS-5 pattern, following
    /// DoctorTreatmentCatalog). Canon Part V equipment survey: the barber's core
    /// is the chair plus the kit (razors, strop, clippers, shears, combs,
    /// brushes, mugs, towels, basin, mirror); "multiple chairs" and "bath
    /// tubs/hot-water capability" are the scale column. Execution fees live in
    /// <see cref="BarberFeeSchedule"/>; task definitions never carry live prices.
    /// </summary>
    public static class BarberServiceCatalog
    {
        public const string ShaveId = "barb.shave";
        public const string HaircutId = "barb.haircut";
        public const string BathId = "barb.bath";

        /// <summary>W1B: item ids barber tasks declare as inputs. Resolved to real lots by the runtime.</summary>
        public const string SoapItemId = "barber-soap";

        /// <summary>W1B: item ids barber tasks declare as inputs. Resolved to real lots by the runtime.</summary>
        public const string LinenItemId = "barber-linen";

        public const string BarberingSkillId = "barbering";

        /// <summary>W1B: the workstation id services require. Defined in WorkstationCatalog; chairs are pooled by the shop runtime.</summary>
        public const string BarberChairStationId = "barber-chair-station";

        /// <summary>
        /// D1B: the bath-tub workstation id. Defined in WorkstationCatalog; tubs
        /// are pooled by the shop runtime. Canon Part V barber profile: "bath
        /// tubs/hot-water capability" is the scale column.
        /// </summary>
        public const string BarberBathStationId = "barber-bath-station";

        /// <summary>D1B: equipment kind a bath-tub component asset carries.</summary>
        public const string BathTubEquipmentKind = "bath-tub";

        /// <summary>D1B: infrastructure id for the hot-water capability a bath needs (stove/boiler/kettle on the premises).</summary>
        public const string HotWaterInfrastructureId = "hot-water";

        /// <summary>TUNING: minutes a shave occupies a chair (labor equals occupancy).</summary>
        public const int ShaveMinutes = 20;

        /// <summary>TUNING: minutes a haircut occupies a chair (labor equals occupancy).</summary>
        public const int HaircutMinutes = 30;

        /// <summary>TUNING: minutes a bath occupies a chair/tub.</summary>
        public const int BathMinutes = 45;

        /// <summary>TUNING: the barber's hands-on minutes for a bath — the customer soaks the rest.</summary>
        public const int BathLaborMinutes = 15;

        /// <summary>TUNING: soap portions per shave.</summary>
        public const int ShaveSoapUnits = 1;

        /// <summary>TUNING: linen units per shave (towel wear, loss, laundering attrition).</summary>
        public const int ShaveLinenUnits = 1;

        /// <summary>TUNING: a dry cut uses no soap.</summary>
        public const int HaircutSoapUnits = 0;

        /// <summary>TUNING: linen units per haircut (neck cloth / towel).</summary>
        public const int HaircutLinenUnits = 1;

        /// <summary>TUNING: soap portions per bath.</summary>
        public const int BathSoapUnits = 2;

        /// <summary>TUNING: linen units per bath (wash towels).</summary>
        public const int BathLinenUnits = 2;

        /// <summary>Service specs as data, for the shop runtime's scheduler.</summary>
        public static BarberServiceSpec GetSpec(string serviceId)
        {
            if (string.Equals(serviceId, ShaveId, StringComparison.Ordinal))
                return new BarberServiceSpec
                {
                    ServiceId = ShaveId, DisplayName = "Shave",
                    ChairMinutes = ShaveMinutes, LaborMinutes = ShaveMinutes,
                    SoapUnits = ShaveSoapUnits, LinenUnits = ShaveLinenUnits,
                };
            if (string.Equals(serviceId, HaircutId, StringComparison.Ordinal))
                return new BarberServiceSpec
                {
                    ServiceId = HaircutId, DisplayName = "Haircut",
                    ChairMinutes = HaircutMinutes, LaborMinutes = HaircutMinutes,
                    SoapUnits = HaircutSoapUnits, LinenUnits = HaircutLinenUnits,
                };
            if (string.Equals(serviceId, BathId, StringComparison.Ordinal))
                return new BarberServiceSpec
                {
                    ServiceId = BathId, DisplayName = "Bath",
                    ChairMinutes = BathMinutes, LaborMinutes = BathLaborMinutes,
                    SoapUnits = BathSoapUnits, LinenUnits = BathLinenUnits,
                };
            return default;
        }

        /// <summary>True for shave/haircut/bath; false for anything else (unknown ids never schedule).</summary>
        public static bool IsKnownService(string serviceId)
        {
            return !string.IsNullOrEmpty(GetSpec(serviceId).ServiceId);
        }

        /// <summary>Reads the fee for a service from the schedule. Unknown services price at zero, never guessed.</summary>
        public static int GetFeeCents(string serviceId, BarberFeeSchedule fees)
        {
            fees = fees ?? new BarberFeeSchedule();
            if (string.Equals(serviceId, ShaveId, StringComparison.Ordinal)) return fees.ShaveFeeCents;
            if (string.Equals(serviceId, HaircutId, StringComparison.Ordinal)) return fees.HaircutFeeCents;
            if (string.Equals(serviceId, BathId, StringComparison.Ordinal)) return fees.BathFeeCents;
            return 0;
        }

        /// <summary>Registers the barbering skill via the TTS-3 extension path.</summary>
        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("BarberServiceCatalog: no SkillService — barbering skill not registered.");
                return;
            }

            if (skillService.GetSkill(BarberingSkillId) != null) return;
            if (!skillService.RegisterSkill(new SkillDefinition(BarberingSkillId, "Barbering",
                "Professional barbering: shaves, haircuts, baths; razor work and strop maintenance. (TTS-3 extension path; Canon Part V barber.)"),
                out string rejection))
            {
                diagnostics.Add($"BarberServiceCatalog: skill '{BarberingSkillId}' rejected: {rejection}");
            }
        }

        /// <summary>Registers the three service task definitions. Safe to call once at boot.</summary>
        public static void RegisterAll(TaskAuthority authority)
        {
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            string ignored;

            // NX-1 teeth gate: every service requires a usable barber's kit
            // (razors dull — the strop in the kit is the maintenance loop).
            string kit = EquipmentRequirementCodes.Kit("barber-kit");
            // Chair gate as data: service happens at a barber chair station;
            // the shop runtime pools chairs and assigns them per service.
            string chair = EquipmentRequirementCodes.Workstation(BarberChairStationId);

            var shave = new TaskDefinition(ShaveId, "Barber shave", ShaveMinutes);
            shave.SetRequiredSkill(BarberingSkillId, new[] { BarberingSkillId });
            shave.SetDefaultPriority(TaskPriority.Normal);
            shave.DomainTags.Add("personal-service");
            shave.DomainTags.Add("grooming");
            shave.EquipmentClasses.Add(kit);
            shave.EquipmentClasses.Add(chair);
            shave.Inputs.Add(new TaskMaterial(SoapItemId, ShaveSoapUnits));
            shave.Inputs.Add(new TaskMaterial(LinenItemId, ShaveLinenUnits));
            authority.RegisterDefinition(shave, out ignored);

            var haircut = new TaskDefinition(HaircutId, "Barber haircut", HaircutMinutes);
            haircut.SetRequiredSkill(BarberingSkillId, new[] { BarberingSkillId });
            haircut.SetDefaultPriority(TaskPriority.Normal);
            haircut.DomainTags.Add("personal-service");
            haircut.DomainTags.Add("grooming");
            haircut.EquipmentClasses.Add(kit);
            haircut.EquipmentClasses.Add(chair);
            haircut.Inputs.Add(new TaskMaterial(LinenItemId, HaircutLinenUnits));
            authority.RegisterDefinition(haircut, out ignored);

            var bath = new TaskDefinition(BathId, "Barber bath", BathMinutes);
            bath.SetRequiredSkill(BarberingSkillId, new[] { BarberingSkillId });
            bath.SetDefaultPriority(TaskPriority.Normal);
            bath.DomainTags.Add("personal-service");
            bath.DomainTags.Add("grooming");
            bath.DomainTags.Add("bathing");
            bath.EquipmentClasses.Add(kit);
            bath.EquipmentClasses.Add(chair);
            bath.Inputs.Add(new TaskMaterial(SoapItemId, BathSoapUnits));
            bath.Inputs.Add(new TaskMaterial(LinenItemId, BathLinenUnits));
            authority.RegisterDefinition(bath, out ignored);
        }
    }
}
