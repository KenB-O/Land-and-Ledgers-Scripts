using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W1A: doctor treatment tasks register through the task system (TTS-5).
    /// </summary>
    [TestFixture]
    public sealed class DoctorTreatmentCatalogTests
    {
        [Test]
        public void RegisterSkills_RegistersDoctoringSkill_Idempotent()
        {
            var skills = new SkillService();
            var diag = new List<string>();

            DoctorTreatmentCatalog.RegisterSkills(skills, diag);
            Assert.NotNull(skills.GetSkill(DoctorTreatmentCatalog.DoctoringSkillId),
                "The doctoring skill must register.");

            DoctorTreatmentCatalog.RegisterSkills(skills, diag);
            Assert.NotNull(skills.GetSkill(DoctorTreatmentCatalog.DoctoringSkillId),
                "Re-registration must be safe (no duplicate rejection).");
        }

        [Test]
        public void RegisterAll_RegistersFourTreatmentDefinitions()
        {
            var authority = new TaskAuthority();
            DoctorTreatmentCatalog.RegisterAll(authority);

            TaskDefinition office = authority.GetDefinition(DoctorTreatmentCatalog.OfficeVisitId);
            TaskDefinition call = authority.GetDefinition(DoctorTreatmentCatalog.HouseCallId);
            TaskDefinition dispense = authority.GetDefinition(DoctorTreatmentCatalog.DispenseRemedyId);
            TaskDefinition procedure = authority.GetDefinition(DoctorTreatmentCatalog.MinorProcedureId);

            Assert.NotNull(office, "Office visit must register.");
            Assert.NotNull(call, "House call must register.");
            Assert.NotNull(dispense, "Dispense remedy must register.");
            Assert.NotNull(procedure, "Minor procedure must register.");

            Assert.AreEqual(DoctorTreatmentCatalog.OfficeVisitMinutes, office.BaseMinutes);
            Assert.AreEqual(DoctorTreatmentCatalog.HouseCallBedsideMinutes, call.BaseMinutes);
            Assert.AreEqual(DoctorTreatmentCatalog.DispenseRemedyMinutes, dispense.BaseMinutes);
            Assert.AreEqual(DoctorTreatmentCatalog.MinorProcedureMinutes, procedure.BaseMinutes);
        }

        [Test]
        public void RegisterAll_TreatmentsRequireDoctoringSkillAndDoctorsBag()
        {
            var authority = new TaskAuthority();
            DoctorTreatmentCatalog.RegisterAll(authority);

            string[] ids =
            {
                DoctorTreatmentCatalog.OfficeVisitId,
                DoctorTreatmentCatalog.HouseCallId,
                DoctorTreatmentCatalog.DispenseRemedyId,
                DoctorTreatmentCatalog.MinorProcedureId,
            };

            foreach (string id in ids)
            {
                TaskDefinition def = authority.GetDefinition(id);
                Assert.AreEqual(DoctorTreatmentCatalog.DoctoringSkillId, def.RequiredSkillId,
                    $"{id}: treatments require the doctoring skill.");
                Assert.Contains("kit:doctor-bag", def.EquipmentClasses,
                    $"{id}: NX-1 teeth gate — treatment needs a usable doctor's bag.");
            }
        }

        [Test]
        public void RegisterAll_MedicineInputsDeclaredAsData()
        {
            var authority = new TaskAuthority();
            DoctorTreatmentCatalog.RegisterAll(authority);

            TaskDefinition dispense = authority.GetDefinition(DoctorTreatmentCatalog.DispenseRemedyId);
            Assert.AreEqual(1, dispense.Inputs.Count);
            Assert.AreEqual(DoctorTreatmentCatalog.MedicineDoseItemId, dispense.Inputs[0].ItemId);

            TaskDefinition procedure = authority.GetDefinition(DoctorTreatmentCatalog.MinorProcedureId);
            Assert.AreEqual(1, procedure.Inputs.Count);
            Assert.AreEqual(DoctorTreatmentCatalog.DressingItemId, procedure.Inputs[0].ItemId);
        }

        [Test]
        public void RegisterAll_HouseCallIsPriorityTravelWork()
        {
            var authority = new TaskAuthority();
            DoctorTreatmentCatalog.RegisterAll(authority);

            TaskDefinition call = authority.GetDefinition(DoctorTreatmentCatalog.HouseCallId);
            Assert.AreEqual(TaskPriority.High, call.DefaultPriority, "Urgent calls outrank routine work.");
            Assert.Contains("travel", call.DomainTags);
        }
    }
}
