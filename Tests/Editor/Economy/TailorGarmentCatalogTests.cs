using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Tailor;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W1C: garment orders register through the task system (TTS-5), with the
    /// piece-rate schedule as data and the NX-1 teeth gate on the tailor's hand kit.
    /// </summary>
    [TestFixture]
    public sealed class TailorGarmentCatalogTests
    {
        [Test]
        public void RegisterSkills_RegistersTailoringSkill_Idempotent()
        {
            var skills = new SkillService();
            var diag = new List<string>();

            TailorGarmentCatalog.RegisterSkills(skills, diag);
            Assert.NotNull(skills.GetSkill(TailorGarmentCatalog.TailoringSkillId),
                "The tailoring skill must register.");

            TailorGarmentCatalog.RegisterSkills(skills, diag);
            Assert.NotNull(skills.GetSkill(TailorGarmentCatalog.TailoringSkillId),
                "Re-registration must be safe (no duplicate rejection).");
        }

        [Test]
        public void RegisterAll_RegistersStageTasks_ForEveryGarment()
        {
            var authority = new TaskAuthority();
            TailorGarmentCatalog.RegisterAll(authority);

            string[] garments =
            {
                TailorGarmentCatalog.ShirtId, TailorGarmentCatalog.TrousersId,
                TailorGarmentCatalog.CoatId, TailorGarmentCatalog.DressId,
            };
            string[] stages = { "measure", "cut", "fit-baste", "sew", "fit-final", "press" };

            foreach (string garment in garments)
            {
                TailorGarmentSpec spec = TailorGarmentCatalog.GetSpec(garment);
                foreach (string stage in stages)
                {
                    string taskId = TailorGarmentCatalog.StageTaskId(stage, garment);
                    Assert.NotNull(authority.GetDefinition(taskId),
                        $"Stage task {taskId} must register.");
                }

                // Spot-check honest base minutes for one garment.
                if (garment == TailorGarmentCatalog.ShirtId)
                {
                    Assert.AreEqual(TailorGarmentCatalog.ShirtCutMinutes,
                        authority.GetDefinition(TailorGarmentCatalog.StageTaskId("cut", garment)).BaseMinutes);
                    Assert.AreEqual(TailorGarmentCatalog.ShirtSewMinutes,
                        authority.GetDefinition(TailorGarmentCatalog.StageTaskId("sew", garment)).BaseMinutes);
                }
            }

            Assert.NotNull(authority.GetDefinition(TailorGarmentCatalog.AssessMendTaskId),
                "Mending assessment must register.");
            Assert.NotNull(authority.GetDefinition(TailorGarmentCatalog.MendTaskId),
                "Mending must register.");
        }

        [Test]
        public void RegisterAll_StagesRequireTailoringSkillAndHandKit()
        {
            var authority = new TaskAuthority();
            TailorGarmentCatalog.RegisterAll(authority);

            string[] taskIds =
            {
                TailorGarmentCatalog.StageTaskId("measure", TailorGarmentCatalog.ShirtId),
                TailorGarmentCatalog.StageTaskId("cut", TailorGarmentCatalog.CoatId),
                TailorGarmentCatalog.StageTaskId("fit-baste", TailorGarmentCatalog.DressId),
                TailorGarmentCatalog.StageTaskId("sew", TailorGarmentCatalog.TrousersId),
                TailorGarmentCatalog.StageTaskId("fit-final", TailorGarmentCatalog.ShirtId),
                TailorGarmentCatalog.StageTaskId("press", TailorGarmentCatalog.CoatId),
                TailorGarmentCatalog.MendTaskId,
            };

            foreach (string id in taskIds)
            {
                TaskDefinition def = authority.GetDefinition(id);
                Assert.AreEqual(TailorGarmentCatalog.TailoringSkillId, def.RequiredSkillId,
                    $"{id}: stages require the tailoring skill.");
                Assert.Contains("kit:tailor-hand-kit", def.EquipmentClasses,
                    $"{id}: NX-1 teeth gate — stages need a usable tailor's hand kit.");
                Assert.Contains("personal-service", def.DomainTags);
                Assert.Contains("garment", def.DomainTags);
            }
        }

        [Test]
        public void RegisterAll_BenchStagesRequireCuttingTable_FittingsDoNot()
        {
            var authority = new TaskAuthority();
            TailorGarmentCatalog.RegisterAll(authority);

            string[] benchStages =
            {
                TailorGarmentCatalog.StageTaskId("cut", TailorGarmentCatalog.ShirtId),
                TailorGarmentCatalog.StageTaskId("sew", TailorGarmentCatalog.ShirtId),
                TailorGarmentCatalog.StageTaskId("press", TailorGarmentCatalog.ShirtId),
                TailorGarmentCatalog.MendTaskId,
            };
            foreach (string id in benchStages)
            {
                Assert.Contains("workstation:tailor-cutting-table",
                    authority.GetDefinition(id).EquipmentClasses,
                    $"{id}: bench work happens at a cutting table station.");
            }

            string[] fittingStages =
            {
                TailorGarmentCatalog.StageTaskId("measure", TailorGarmentCatalog.ShirtId),
                TailorGarmentCatalog.StageTaskId("fit-baste", TailorGarmentCatalog.ShirtId),
                TailorGarmentCatalog.StageTaskId("fit-final", TailorGarmentCatalog.ShirtId),
            };
            foreach (string id in fittingStages)
            {
                Assert.IsFalse(authority.GetDefinition(id).EquipmentClasses
                        .Contains("workstation:tailor-cutting-table"),
                    $"{id}: fittings need the customer, not a table.");
                Assert.Contains("fitting", authority.GetDefinition(
                    TailorGarmentCatalog.StageTaskId("fit-baste", TailorGarmentCatalog.ShirtId)).DomainTags,
                    "Fitting stages carry the fitting domain tag.");
            }
        }

        [Test]
        public void RegisterAll_CutTasksDeclareClothInputs_PerGarmentYards()
        {
            var authority = new TaskAuthority();
            TailorGarmentCatalog.RegisterAll(authority);

            var expected = new Dictionary<string, int>
            {
                { TailorGarmentCatalog.ShirtId, TailorGarmentCatalog.ShirtClothYards },
                { TailorGarmentCatalog.TrousersId, TailorGarmentCatalog.TrousersClothYards },
                { TailorGarmentCatalog.CoatId, TailorGarmentCatalog.CoatClothYards },
                { TailorGarmentCatalog.DressId, TailorGarmentCatalog.DressClothYards },
            };

            foreach (var kvp in expected)
            {
                TaskDefinition cut = authority.GetDefinition(
                    TailorGarmentCatalog.StageTaskId("cut", kvp.Key));
                Assert.AreEqual(1, cut.Inputs.Count, $"{kvp.Key}: cut declares exactly its cloth input.");
                Assert.AreEqual(TailorGarmentCatalog.ClothItemId, cut.Inputs[0].ItemId);
                Assert.AreEqual(kvp.Value, cut.Inputs[0].Quantity,
                    $"{kvp.Key}: cut declares that garment's cloth yards.");
            }

            TaskDefinition sew = authority.GetDefinition(
                TailorGarmentCatalog.StageTaskId("sew", TailorGarmentCatalog.CoatId));
            Assert.AreEqual(TailorGarmentCatalog.NotionsItemId, sew.Inputs[0].ItemId);
            Assert.AreEqual(TailorGarmentCatalog.HeavyNotionsUnits, sew.Inputs[0].Quantity);
        }

        [Test]
        public void GetSpec_UnknownGarment_ReturnsEmpty_NeverGuessed()
        {
            Assert.IsFalse(TailorGarmentCatalog.IsKnownGarment("tail.ballgown"),
                "Unknown garments never schedule.");
            Assert.IsTrue(string.IsNullOrEmpty(TailorGarmentCatalog.GetSpec("tail.ballgown").GarmentId));
            Assert.IsTrue(TailorGarmentCatalog.IsKnownGarment(TailorGarmentCatalog.MendId));
            Assert.IsTrue(TailorGarmentCatalog.GetSpec(TailorGarmentCatalog.MendId).IsMending);
        }

        [Test]
        public void GetPieceRateCents_ReadsSchedule_UnknownPricesAtZero()
        {
            var rates = new TailorPieceRateSchedule(111, 222, 333, 444, 55, 11);

            Assert.AreEqual(111, TailorGarmentCatalog.GetPieceRateCents(TailorGarmentCatalog.ShirtId, rates));
            Assert.AreEqual(222, TailorGarmentCatalog.GetPieceRateCents(TailorGarmentCatalog.TrousersId, rates));
            Assert.AreEqual(333, TailorGarmentCatalog.GetPieceRateCents(TailorGarmentCatalog.CoatId, rates));
            Assert.AreEqual(444, TailorGarmentCatalog.GetPieceRateCents(TailorGarmentCatalog.DressId, rates));
            Assert.AreEqual(55, TailorGarmentCatalog.GetPieceRateCents(TailorGarmentCatalog.MendId, rates));
            Assert.AreEqual(0, TailorGarmentCatalog.GetPieceRateCents("tail.ballgown", rates),
                "Unknown garments price at zero, never guessed.");
            Assert.AreEqual(0, TailorGarmentCatalog.GetPieceRateCents(TailorGarmentCatalog.ShirtId, null),
                "A null schedule prices at zero — rates are data, never embedded in the catalog.");
        }
    }
}
