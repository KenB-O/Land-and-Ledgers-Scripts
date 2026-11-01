using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// Pass III Worker D: skill registry (extension path preserved), continuous
    /// capability model, experience formation, and causal task-duration
    /// estimation.
    ///
    /// CANON CORRECTION (documented per task): the XP/level ladder (1 XP/min,
    /// 100 XP/level, max 10) and the flat "-1 minute per level" duration rule
    /// were canon-violating — Rev VII §1.1 forbids level-up progression and
    /// Tech XIV Part II §2.4 forbids flat SkillMultiplier shortcuts. They are
    /// replaced by continuous capability 0..1, the experience ledger with
    /// diminishing-returns formation, and the causal outcome resolver.
    /// XXXI §4.5 CANON LOCK: the seven-band display is superseded (see
    /// CapabilityPresentationTests).
    /// </summary>
    [TestFixture]
    public sealed class SkillServiceTests
    {
        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        [Test]
        public void UnknownPersonOrSkill_DefaultsToNoviceCapability()
        {
            var service = new SkillService();
            service.RegisterStarterSet();

            Assert.AreEqual(PersonCapabilityStore.NoviceCapability,
                service.GetCapability(Person(99), SkillIds.Cooking), 1e-9);
            Assert.AreEqual(PersonCapabilityStore.NoviceCapability,
                service.GetCapability(Person(99), "never-registered-skill"), 1e-9);
            // Legacy level projection: novice is level 1.
            Assert.AreEqual(1, service.GetLevel(Person(99), SkillIds.Cooking));
        }

        [Test]
        public void RegisterSkill_Duplicate_Rejected_CustomSkill_Accepted()
        {
            var service = new SkillService();
            service.RegisterStarterSet();

            string reason;
            Assert.IsFalse(service.RegisterSkill(new SkillDefinition(SkillIds.Cooking, "Cooking 2"), out reason));
            Assert.IsNotNull(reason);

            // Extension path: a brand-new skill registers without touching existing code.
            Assert.IsTrue(service.RegisterSkill(
                new SkillDefinition("blacksmithing", "Blacksmithing", null, CapabilityFamily.MechanicalRepair),
                out reason), reason);
            Assert.IsNotNull(service.GetSkill("blacksmithing"));
            Assert.AreEqual(CapabilityFamily.MechanicalRepair,
                service.GetSkill("blacksmithing").Family);
            Assert.AreEqual(1, service.GetLevel(Person(2), "blacksmithing"));
        }

        [Test]
        public void CausalEstimator_VariesDuration_ByWorkerCapability()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();

            var authority = new TaskAuthority();
            string reason;
            var tend = new TaskDefinition("gs.tend", "Tend customer", 60);
            tend.SetRequiredSkill(SkillIds.CustomerTending, new[] { SkillIds.CustomerTending });
            Assert.IsTrue(authority.RegisterDefinition(tend, out reason), reason);
            authority.SetDurationEstimator(new CausalTaskDurationEstimator(skills));

            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            // Novice (capability 0.10): slower than base.
            WorkTask novice = authority.CreateTask("gs.tend", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(authority.AssignTask(novice.TaskId, Person(10), budgets, out reason), reason);
            Assert.Greater(novice.PlannedMinutes, 60);

            // Experienced worker: real capability (0.75, Great).
            // Set directly — this test targets the estimator, not formation
            // speed (formation is covered in CapabilityStoresTests).
            EntityId veteran = Person(11);
            skills.Capabilities.SetCapability(veteran.ToString(), SkillIds.CustomerTending, 0.75, 40);
            // And some real experience relevance.
            for (int day = 1; day <= 25; day++)
            {
                var inputs = new ExperienceFormationInputs
                {
                    TaskCapabilityId = SkillIds.CustomerTending,
                    Difficulty01 = 0.7,
                    Novelty01 = 0.9,
                    Supervised = false,
                    Outcome = "Success",
                    MinutesSpent = 480,
                    Day = day,
                };
                skills.RecordTaskExperience(veteran, inputs);
            }
            double veteranCapability = skills.GetCapability(veteran, SkillIds.CustomerTending);
            Assert.Greater(veteranCapability, 0.7);

            WorkTask veteranTask = authority.CreateTask("gs.tend", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(authority.AssignTask(veteranTask.TaskId, veteran, budgets, out reason), reason);
            // The experienced worker is faster than the novice on the same task.
            Assert.Less(veteranTask.PlannedMinutes, novice.PlannedMinutes);
        }

        [Test]
        public void ApplyExperienceFromTask_RecordsLedgerEntries_ForLearningTags()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();

            var authority = new TaskAuthority();
            string reason;
            var sweep = new TaskDefinition("test.sweep", "Sweep", 10);
            sweep.SetRequiredSkill(SkillIds.Cleaning, new[] { SkillIds.Cleaning });
            Assert.IsTrue(authority.RegisterDefinition(sweep, out reason), reason);

            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            WorkTask task = authority.CreateTask("test.sweep", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(20), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.IsTrue(authority.RecordWork(task.TaskId, 10, budgets, 1, out reason), reason);

            double before = skills.GetCapability(Person(20), SkillIds.Cleaning);
            skills.ApplyExperienceFromTask(
                authority, task.TaskId,
                difficulty01: 0.4, novelty01: 0.9, supervised: false,
                outcome: "Success", day: 1, minutesWorked: 10);

            // One ledger entry for the learning tag...
            Assert.AreEqual(1, skills.Experience.Count);
            // ...and capability moved (no XP ladder; continuous formation).
            Assert.Greater(skills.GetCapability(Person(20), SkillIds.Cleaning), before);
        }

        [Test]
        public void Aptitude_AcceleratesLearning_WithoutGrantingStartingSkill()
        {
            var service = new SkillService();
            service.RegisterStarterSet();

            EntityId quick = Person(30);
            EntityId slow = Person(31);
            service.SetAptitude(quick, CapabilityFamily.FoodProcessing, 2.0, 1);
            service.SetAptitude(slow, CapabilityFamily.FoodProcessing, 0.5, 1);

            // Aptitude grants NO starting capability.
            Assert.AreEqual(service.GetCapability(quick, SkillIds.Cooking),
                service.GetCapability(slow, SkillIds.Cooking), 1e-9);

            // Same work: the quick learner gains more.
            for (int i = 0; i < 2; i++)
            {
                EntityId who = i == 0 ? quick : slow;
                var inputs = new ExperienceFormationInputs
                {
                    TaskCapabilityId = SkillIds.Cooking,
                    Difficulty01 = 0.6,
                    Novelty01 = 1.0,
                    Supervised = false,
                    Outcome = "Success",
                    MinutesSpent = 480,
                    Day = 10 + i,
                };
                service.RecordTaskExperience(who, inputs);
            }

            double quickCap = service.GetCapability(quick, SkillIds.Cooking);
            double slowCap = service.GetCapability(slow, SkillIds.Cooking);
            Assert.Greater(quickCap, slowCap);
        }

        [Test]
        public void SaveLoad_RoundTrips_NewStores_WithoutDoubleCredit()
        {
            var service = new SkillService();
            service.RegisterStarterSet();

            EntityId worker = Person(40);
            service.SetAptitude(worker, CapabilityFamily.Agricultural, 1.5, 1);
            var inputs = new ExperienceFormationInputs
            {
                TaskCapabilityId = SkillIds.CropTending,
                Difficulty01 = 0.6,
                Novelty01 = 0.8,
                Supervised = true,
                Outcome = "Success",
                MinutesSpent = 240,
                Day = 5,
            };
            Assert.IsTrue(service.RecordTaskExperience(worker, inputs));
            var second = new ExperienceFormationInputs
            {
                TaskCapabilityId = SkillIds.CropTending,
                Difficulty01 = 0.6,
                Novelty01 = 0.8,
                Supervised = true,
                Outcome = "Success",
                MinutesSpent = 240,
                Day = 6,
            };
            Assert.IsTrue(service.RecordTaskExperience(worker, second));
            Assert.AreEqual(2, service.Experience.Count);

            double capabilityBefore = service.GetCapability(worker, SkillIds.CropTending);

            var dto = service.CaptureSaveDto();
            var restored = new SkillService();
            restored.RegisterStarterSet();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(2, restored.Experience.Count);
            Assert.AreEqual(capabilityBefore,
                restored.GetCapability(worker, SkillIds.CropTending), 1e-9);
            Assert.AreEqual(1.5,
                restored.Aptitudes.GetLearningRate(worker.ToString(), CapabilityFamily.Agricultural), 1e-9);

            // Restoring the SAME dto again does not duplicate or re-credit.
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(2, restored.Experience.Count);
            Assert.AreEqual(capabilityBefore,
                restored.GetCapability(worker, SkillIds.CropTending), 1e-9);
        }
    }
}
