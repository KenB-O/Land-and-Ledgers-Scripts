// Plan 12 (2026-11-13, Slice Request 3): pins the shared freight
// handling-task predicate. The predicate's application sites live in
// SimulationDrivers (harness-excluded) and are verified by
// inspection against this table plus the full-harness gate, not by a
// harness test (Plan 05 precedent).
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    [TestFixture]
    public sealed class FreightHandlingTaskGuardTests
    {
        private static readonly EntityId FreightOwner = EntityId.For(EntityKind.Business, 3);
        private static readonly EntityId OtherOwner = EntityId.For(EntityKind.Business, 4);

        private static WorkTask MakeTask(string definitionId, EntityId owner)
        {
            var authority = new TaskAuthority();
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(
                definitionId, definitionId, 30), out string reason), reason);
            return authority.CreateTaskWithPlannedMinutes(definitionId, owner, 1, 30);
        }

        [TestCase("freight:load:ship-1")]
        [TestCase("freight:unload:ship-1")]
        [TestCase("fr.load-freight")]
        [TestCase("fr.unload-freight")]
        public void FreightDefinitionId_Skips(string definitionId)
        {
            WorkTask task = MakeTask(definitionId, OtherOwner);
            Assert.IsTrue(FreightHandlingTaskGuard.IsFreightHandlingTask(task),
                "a freight load/unload definition id skips whatever its owner");
        }

        [TestCase("ordinary-work")]
        [TestCase("fr.load-freight")]
        public void FreightOwner_Skips(string definitionId)
        {
            WorkTask task = MakeTask(definitionId, FreightOwner);
            Assert.IsTrue(FreightHandlingTaskGuard.IsFreightHandlingTask(task),
                "a task owned by the freight business (id 3) is a handling task, whatever its definition id");
        }

        [TestCase("ordinary-work", 4)]
        [TestCase("freight-dispatch-assignment", 4)]
        [TestCase("freight-dispatch-assignment", 2060095228)]
        [TestCase("freight-service-meeting", 4)]
        [TestCase("gs.unload-freight", 4)]
        [TestCase("fr.repair-wagon", 4)]
        [TestCase("freight:load", 4)]
        public void NonFreightTask_DoesNotSkip(string definitionId, int ownerBusinessId)
        {
            WorkTask task = MakeTask(definitionId, EntityId.For(EntityKind.Business, ownerBusinessId));
            Assert.IsTrue(!FreightHandlingTaskGuard.IsFreightHandlingTask(task),
                "a non-handling task must not skip: '" + definitionId + "'");
        }

        [Test]
        public void NullTask_DoesNotSkip()
        {
            Assert.IsTrue(!FreightHandlingTaskGuard.IsFreightHandlingTask(null),
                "a null task is not a handling task");
        }
    }
}
