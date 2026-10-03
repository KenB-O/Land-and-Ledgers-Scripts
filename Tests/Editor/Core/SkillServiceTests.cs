using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// TTS-3: skill registry with extension path, rudimentary XP, and skill-scaled
    /// task durations (higher skill → fewer minutes, floored at 1 — Kennedy's rule).
    /// </summary>
    [TestFixture]
    public sealed class SkillServiceTests
    {
        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        [Test]
        public void ScaleMinutes_HigherSkill_FewerMinutes_FlooredAtOne()
        {
            var service = new SkillService();

            Assert.AreEqual(4, service.ScaleMinutes(4, 1));
            Assert.AreEqual(3, service.ScaleMinutes(4, 2));
            Assert.AreEqual(1, service.ScaleMinutes(4, 4));
            Assert.AreEqual(1, service.ScaleMinutes(4, 10)); // floor holds past the curve
            Assert.AreEqual(1, service.ScaleMinutes(1, 10)); // base-1 task never goes below 1
            Assert.AreEqual(28, service.ScaleMinutes(30, 3));
        }

        [Test]
        public void GrantPractice_AccumulatesXp_AndLevelsUp()
        {
            var service = new SkillService();
            service.RegisterStarterSet();

            Assert.AreEqual(1, service.GetLevel(Person(1), SkillIds.CustomerTending));

            int level = service.GrantPractice(Person(1), SkillIds.CustomerTending, 100);
            Assert.AreEqual(2, level);
            Assert.AreEqual(2, service.GetLevel(Person(1), SkillIds.CustomerTending));

            service.GrantPractice(Person(1), SkillIds.CustomerTending, 150);
            Assert.AreEqual(3, service.GetLevel(Person(1), SkillIds.CustomerTending));
        }

        [Test]
        public void UnknownPersonOrSkill_DefaultsToLevelOne()
        {
            var service = new SkillService();
            service.RegisterStarterSet();

            Assert.AreEqual(1, service.GetLevel(Person(99), SkillIds.Cooking));
            Assert.AreEqual(1, service.GetLevel(Person(99), "never-registered-skill"));
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
            Assert.IsTrue(service.RegisterSkill(new SkillDefinition("blacksmithing", "Blacksmithing"), out reason), reason);
            Assert.IsNotNull(service.GetSkill("blacksmithing"));
            Assert.AreEqual(1, service.GetLevel(Person(2), "blacksmithing"));
        }

        [Test]
        public void Estimator_ScalesAssignment_ByWorkerSkill()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();

            var authority = new TaskAuthority();
            string reason;
            var tend = new TaskDefinition("gs.tend", "Tend customer", 4);
            tend.SetRequiredSkill(SkillIds.CustomerTending, new[] { SkillIds.CustomerTending });
            Assert.IsTrue(authority.RegisterDefinition(tend, out reason), reason);
            authority.SetDurationEstimator(new SkillTaskDurationEstimator(skills));

            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            // Beginner: full 4 minutes.
            WorkTask novice = authority.CreateTask("gs.tend", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(authority.AssignTask(novice.TaskId, Person(10), budgets, out reason), reason);
            Assert.AreEqual(4, novice.PlannedMinutes);

            // Veteran (level 4 = 300 XP): floored at 1 minute.
            skills.GrantPractice(Person(11), SkillIds.CustomerTending, 300);
            Assert.AreEqual(4, skills.GetLevel(Person(11), SkillIds.CustomerTending));
            WorkTask veteran = authority.CreateTask("gs.tend", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(authority.AssignTask(veteran.TaskId, Person(11), budgets, out reason), reason);
            Assert.AreEqual(1, veteran.PlannedMinutes);
        }

        [Test]
        public void ApplyPracticeFromTask_GrantsXp_ForLearningTags()
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

            skills.ApplyPracticeFromTask(authority, task.TaskId, 10);
            Assert.AreEqual(1, skills.GetLevel(Person(20), SkillIds.Cleaning)); // 10 XP < 100
            skills.ApplyPracticeFromTask(authority, task.TaskId, 90);
            Assert.AreEqual(2, skills.GetLevel(Person(20), SkillIds.Cleaning)); // 100 XP total
        }
    }
}
