using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Barber;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W1B: barber services register through the task system (TTS-5), with the
    /// fee schedule as data and the NX-1 teeth gate on the barber's kit.
    /// </summary>
    [TestFixture]
    public sealed class BarberServiceCatalogTests
    {
        [Test]
        public void RegisterSkills_RegistersBarberingSkill_Idempotent()
        {
            var skills = new SkillService();
            var diag = new List<string>();

            BarberServiceCatalog.RegisterSkills(skills, diag);
            Assert.NotNull(skills.GetSkill(BarberServiceCatalog.BarberingSkillId),
                "The barbering skill must register.");

            BarberServiceCatalog.RegisterSkills(skills, diag);
            Assert.NotNull(skills.GetSkill(BarberServiceCatalog.BarberingSkillId),
                "Re-registration must be safe (no duplicate rejection).");
        }

        [Test]
        public void RegisterAll_RegistersThreeServiceDefinitions()
        {
            var authority = new TaskAuthority();
            BarberServiceCatalog.RegisterAll(authority);

            TaskDefinition shave = authority.GetDefinition(BarberServiceCatalog.ShaveId);
            TaskDefinition haircut = authority.GetDefinition(BarberServiceCatalog.HaircutId);
            TaskDefinition bath = authority.GetDefinition(BarberServiceCatalog.BathId);

            Assert.NotNull(shave, "Shave must register.");
            Assert.NotNull(haircut, "Haircut must register.");
            Assert.NotNull(bath, "Bath must register.");

            Assert.AreEqual(BarberServiceCatalog.ShaveMinutes, shave.BaseMinutes);
            Assert.AreEqual(BarberServiceCatalog.HaircutMinutes, haircut.BaseMinutes);
            Assert.AreEqual(BarberServiceCatalog.BathMinutes, bath.BaseMinutes);
        }

        [Test]
        public void RegisterAll_ServicesRequireBarberingSkillKitAndChairStation()
        {
            var authority = new TaskAuthority();
            BarberServiceCatalog.RegisterAll(authority);

            string[] ids =
            {
                BarberServiceCatalog.ShaveId,
                BarberServiceCatalog.HaircutId,
                BarberServiceCatalog.BathId,
            };

            foreach (string id in ids)
            {
                TaskDefinition def = authority.GetDefinition(id);
                Assert.AreEqual(BarberServiceCatalog.BarberingSkillId, def.RequiredSkillId,
                    $"{id}: services require the barbering skill.");
                Assert.Contains("kit:barber-kit", def.EquipmentClasses,
                    $"{id}: NX-1 teeth gate — service needs a usable barber's kit.");
                Assert.Contains("workstation:barber-chair-station", def.EquipmentClasses,
                    $"{id}: service happens at a barber chair station.");
                Assert.Contains("personal-service", def.DomainTags, $"{id}: personal-service tag.");
            }
        }

        [Test]
        public void RegisterAll_ConsumableInputsDeclaredAsData()
        {
            var authority = new TaskAuthority();
            BarberServiceCatalog.RegisterAll(authority);

            TaskDefinition shave = authority.GetDefinition(BarberServiceCatalog.ShaveId);
            Assert.AreEqual(2, shave.Inputs.Count, "Shave: soap + linen.");
            Assert.AreEqual(BarberServiceCatalog.SoapItemId, shave.Inputs[0].ItemId);
            Assert.AreEqual(BarberServiceCatalog.ShaveSoapUnits, shave.Inputs[0].Quantity);
            Assert.AreEqual(BarberServiceCatalog.LinenItemId, shave.Inputs[1].ItemId);
            Assert.AreEqual(BarberServiceCatalog.ShaveLinenUnits, shave.Inputs[1].Quantity);

            TaskDefinition haircut = authority.GetDefinition(BarberServiceCatalog.HaircutId);
            Assert.AreEqual(1, haircut.Inputs.Count, "Haircut: linen only — a dry cut uses no soap.");
            Assert.AreEqual(BarberServiceCatalog.LinenItemId, haircut.Inputs[0].ItemId);
            Assert.AreEqual(BarberServiceCatalog.HaircutLinenUnits, haircut.Inputs[0].Quantity);

            TaskDefinition bath = authority.GetDefinition(BarberServiceCatalog.BathId);
            Assert.AreEqual(2, bath.Inputs.Count, "Bath: soap + linen.");
            Assert.AreEqual(BarberServiceCatalog.BathSoapUnits, bath.Inputs[0].Quantity);
            Assert.AreEqual(BarberServiceCatalog.BathLinenUnits, bath.Inputs[1].Quantity);
        }

        [Test]
        public void GetSpec_KnownServicesCarryChairLaborAndConsumableData()
        {
            BarberServiceSpec shave = BarberServiceCatalog.GetSpec(BarberServiceCatalog.ShaveId);
            Assert.AreEqual(BarberServiceCatalog.ShaveMinutes, shave.ChairMinutes);
            Assert.AreEqual(BarberServiceCatalog.ShaveMinutes, shave.LaborMinutes,
                "A shave is hands-on for its whole duration.");

            BarberServiceSpec bath = BarberServiceCatalog.GetSpec(BarberServiceCatalog.BathId);
            Assert.AreEqual(BarberServiceCatalog.BathMinutes, bath.ChairMinutes);
            Assert.AreEqual(BarberServiceCatalog.BathLaborMinutes, bath.LaborMinutes);
            Assert.Less(bath.LaborMinutes, bath.ChairMinutes,
                "A bath soaks while the barber works elsewhere — the chair concurrency the pool exists for.");

            Assert.False(BarberServiceCatalog.IsKnownService("barb.perm-wave"),
                "Unknown services never schedule.");
            Assert.False(BarberServiceCatalog.IsKnownService(null));
        }

        [Test]
        public void GetFeeCents_ReadsSchedule_UnknownServicePricesZero()
        {
            var fees = new BarberFeeSchedule(10, 20, 30);

            Assert.AreEqual(10, BarberServiceCatalog.GetFeeCents(BarberServiceCatalog.ShaveId, fees));
            Assert.AreEqual(20, BarberServiceCatalog.GetFeeCents(BarberServiceCatalog.HaircutId, fees));
            Assert.AreEqual(30, BarberServiceCatalog.GetFeeCents(BarberServiceCatalog.BathId, fees));
            Assert.AreEqual(0, BarberServiceCatalog.GetFeeCents("barb.perm-wave", fees),
                "Unknown services price at zero, never guessed.");

            var defaults = new BarberFeeSchedule();
            Assert.Greater(defaults.ShaveFeeCents, 0);
            Assert.Greater(defaults.HaircutFeeCents, 0);
            Assert.Greater(defaults.BathFeeCents, 0);
        }
    }
}
