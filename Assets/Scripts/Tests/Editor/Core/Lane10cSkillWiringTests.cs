using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// Lane 10c (J-S1/J-S4): the 9-skill starter set is registered in
    /// production (not tests only), and completed tasks grant experience
    /// through ApplyExperienceFromTask on the shared completion path.
    ///
    /// J-S1: RegisterStarterSet is called from SimulationSystemsHub
    /// (RegisterTaskCatalogs at boot, and again after every save load so a
    /// Continue from a pre-lane save does not silently lose the set). The
    /// hub itself is harness-excluded, so these tests pin the contract the
    /// hub relies on: all 9 skills register with their capability families,
    /// registration is idempotent, and the load-then-register sequence
    /// restores the set from an old save that carries no definitions.
    ///
    /// J-S4: the production call site is SimulationDrivers.ResolveCausalOutcome
    /// (also harness-excluded — ParseCheck only). These tests pin the exact
    /// sequence that call site performs — ResolveTaskCompletion, then
    /// ApplyExperienceFromTask excluding the required skill — and the
    /// no-double-credit invariant it exists to preserve: authored catalogs
    /// almost always list the required skill as its own learning tag, so an
    /// unfiltered call would credit the same episode twice (the ledger's
    /// idempotency is EntryId-based and cannot catch semantic duplicates).
    /// </summary>
    [TestFixture]
    public sealed class Lane10cSkillWiringTests
    {
        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        private static readonly string[] StarterSkillIds =
        {
            SkillIds.CustomerTending,
            SkillIds.FreightHandling,
            SkillIds.Stocking,
            SkillIds.AnimalHusbandry,
            SkillIds.CropTending,
            SkillIds.Cooking,
            SkillIds.Cleaning,
            SkillIds.BasicRepair,
            SkillIds.Carpentry,
        };

        [Test]
        public void StarterSet_RegistersAllNineSkills_WithCapabilityFamilies()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();

            foreach (string skillId in StarterSkillIds)
            {
                Assert.IsNotNull(skills.GetSkill(skillId), skillId);
                Assert.AreNotEqual(CapabilityFamily.Unknown, skills.GetSkill(skillId).Family, skillId);
            }

            Assert.AreEqual(CapabilityFamily.CommercialInteraction, skills.GetSkill(SkillIds.CustomerTending).Family);
            Assert.AreEqual(CapabilityFamily.Transport, skills.GetSkill(SkillIds.FreightHandling).Family);
            Assert.AreEqual(CapabilityFamily.OrganizationManagement, skills.GetSkill(SkillIds.Stocking).Family);
            Assert.AreEqual(CapabilityFamily.AnimalHandling, skills.GetSkill(SkillIds.AnimalHusbandry).Family);
            Assert.AreEqual(CapabilityFamily.Agricultural, skills.GetSkill(SkillIds.CropTending).Family);
            Assert.AreEqual(CapabilityFamily.FoodProcessing, skills.GetSkill(SkillIds.Cooking).Family);
            Assert.AreEqual(CapabilityFamily.OrganizationManagement, skills.GetSkill(SkillIds.Cleaning).Family);
            Assert.AreEqual(CapabilityFamily.MechanicalRepair, skills.GetSkill(SkillIds.BasicRepair).Family);
            Assert.AreEqual(CapabilityFamily.Construction, skills.GetSkill(SkillIds.Carpentry).Family);
        }

        [Test]
        public void StarterSet_IsIdempotent_AndAptitudeFamilyResolves()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();
            skills.RegisterStarterSet();

            // Still exactly 9 definitions after a second registration.
            Assert.AreEqual(9, skills.CaptureSaveDto().definitions.Count);

            // The J-S1 effect: with the skill registered, aptitude resolves
            // through its family instead of falling back to Unknown (1.0).
            EntityId cook = Person(50);
            skills.SetAptitude(cook, CapabilityFamily.FoodProcessing, 2.0, 1);
            Assert.AreEqual(2.0, skills.GetLearningRate(cook, SkillIds.Cooking), 1e-9);

            // Aptitude is a learning rate only: it grants no starting skill.
            Assert.AreEqual(PersonCapabilityStore.NoviceCapability,
                skills.GetCapability(cook, SkillIds.Cooking), 1e-9);
        }

        [Test]
        public void StarterSet_SurvivesOldSaveLoad_WhenRegisteredAfterLoad()
        {
            // An old save: definitions were never registered in production,
            // so the captured DTO carries none.
            var oldService = new SkillService();
            var oldDto = oldService.CaptureSaveDto();
            Assert.AreEqual(0, oldDto.definitions.Count);

            // The hub's load sequence: clear-then-load, then re-register.
            var restored = new SkillService();
            restored.LoadFromSaveDto(oldDto);
            Assert.IsNull(restored.GetSkill(SkillIds.Cooking));
            restored.RegisterStarterSet();

            foreach (string skillId in StarterSkillIds)
            {
                Assert.IsNotNull(restored.GetSkill(skillId), skillId);
            }
        }

        [Test]
        public void CompletionPath_RequiredSkillCreditedOnce_SecondaryTagGains()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();

            var authority = new TaskAuthority();
            string reason;
            // Required skill cleaning; secondary tag stocking (the pattern
            // a presswork job shows with typesetting).
            var definition = new TaskDefinition("test.lane10c-clean", "Clean and restock", 60);
            definition.SetRequiredSkill(SkillIds.Cleaning, new[] { SkillIds.Cleaning, SkillIds.Stocking });
            Assert.IsTrue(authority.RegisterDefinition(definition, out reason), reason);

            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            EntityId worker = Person(60);
            WorkTask task = authority.CreateTask("test.lane10c-clean", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, worker, budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            bool reachedCompletion;
            Assert.IsTrue(authority.RecordWorkProgress(task.TaskId, 60, budgets, 1, out reachedCompletion, out reason), reason);
            Assert.IsTrue(reachedCompletion);

            double cleaningBefore = skills.GetCapability(worker, SkillIds.Cleaning);
            double stockingBefore = skills.GetCapability(worker, SkillIds.Stocking);

            // The production completion sequence (ResolveCausalOutcome):
            // resolve + record the required skill, then practice the tags.
            CausalOutcomeInputs inputs = skills.BuildCausalInputs(worker, SkillIds.Cleaning, 60);
            TaskOutcome outcome = skills.ResolveTaskCompletion(
                worker, SkillIds.Cleaning, inputs,
                difficulty01: 0.5, novelty01: 0.25, supervised: false,
                day: 1, taskIdForDeterminism: task.TaskId.ToString());
            skills.ApplyExperienceFromTask(
                authority, task.TaskId,
                difficulty01: 0.5, novelty01: 0.25, supervised: false,
                outcome: outcome.ToString(), day: 1, minutesWorked: task.MinutesWorked,
                excludeSkillId: SkillIds.Cleaning);
            Assert.IsTrue(authority.CompleteTask(task.TaskId, outcome, 1, budgets));

            // Exactly two ledger entries: one cleaning (resolver), one
            // stocking (practice hook) — the required skill was NOT credited
            // twice for the same episode.
            Assert.AreEqual(2, skills.Experience.Count);
            Assert.AreEqual(1, skills.Experience.GetEntries(worker.ToString(), SkillIds.Cleaning).Count);
            Assert.AreEqual(1, skills.Experience.GetEntries(worker.ToString(), SkillIds.Stocking).Count);
            // Capability moved from its start value on both skills.
            Assert.Greater(skills.GetCapability(worker, SkillIds.Cleaning), cleaningBefore);
            Assert.Greater(skills.GetCapability(worker, SkillIds.Stocking), stockingBefore);
        }

        [Test]
        public void CompletionPath_TagEqualToRequiredSkill_NoDoubleCredit()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();

            var authority = new TaskAuthority();
            string reason;
            // The common authored pattern: the learning tag IS the required
            // skill (e.g. GeneralStore stock/tend/clean definitions).
            var definition = new TaskDefinition("test.lane10c-stock", "Stock shelves", 60);
            definition.SetRequiredSkill(SkillIds.Stocking, new[] { SkillIds.Stocking });
            Assert.IsTrue(authority.RegisterDefinition(definition, out reason), reason);

            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            EntityId worker = Person(61);
            WorkTask task = authority.CreateTask("test.lane10c-stock", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, worker, budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            bool reachedCompletion;
            Assert.IsTrue(authority.RecordWorkProgress(task.TaskId, 60, budgets, 1, out reachedCompletion, out reason), reason);
            Assert.IsTrue(reachedCompletion);

            double before = skills.GetCapability(worker, SkillIds.Stocking);

            CausalOutcomeInputs inputs = skills.BuildCausalInputs(worker, SkillIds.Stocking, 60);
            TaskOutcome outcome = skills.ResolveTaskCompletion(
                worker, SkillIds.Stocking, inputs,
                difficulty01: 0.5, novelty01: 0.25, supervised: false,
                day: 1, taskIdForDeterminism: task.TaskId.ToString());
            skills.ApplyExperienceFromTask(
                authority, task.TaskId,
                difficulty01: 0.5, novelty01: 0.25, supervised: false,
                outcome: outcome.ToString(), day: 1, minutesWorked: task.MinutesWorked,
                excludeSkillId: SkillIds.Stocking);
            Assert.IsTrue(authority.CompleteTask(task.TaskId, outcome, 1, budgets));

            // One episode = one ledger entry for the skill, not two.
            Assert.AreEqual(1, skills.Experience.Count);
            Assert.AreEqual(1, skills.Experience.GetEntries(worker.ToString(), SkillIds.Stocking).Count);
            // Capability still moved from its start value (0.10 novice).
            Assert.AreEqual(PersonCapabilityStore.NoviceCapability, before, 1e-9);
            Assert.Greater(skills.GetCapability(worker, SkillIds.Stocking), before);
        }
    }
}
