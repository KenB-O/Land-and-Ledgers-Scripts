using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Reporting;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// PKG-4 (PL-51, Phase-0): SD-05 regression fixture. FTE and headcount are read models
    /// derived from executed/planned worker-time (here: actual worker assignments). They must
    /// not become causal WorkerSlot counts that fabricate Persons or suppress seasonal hiring.
    /// </summary>
    [TestFixture]
    public sealed class DerivedStaffingReadModelFixtureTests
    {
        [Test]
        public void VacantRequiredForOpeningSlots_FabricateNoWorkers()
        {
            // A business template with required-for-opening slots but no assigned workers
            // reports zero headcount: vacancies never fabricate Persons.
            var slots = new List<WorkerSlotState>
            {
                new WorkerSlotState("manager", "Manager", 1500, true),
                new WorkerSlotState("clerk", "Clerk", 1000, true),
                new WorkerSlotState("porter", "Porter", 900, false),
            };

            WorkforceStaffingReadModel model = WorkforceStaffingReadModel.FromAssignments("biz-1", slots);

            Assert.AreEqual(0, model.FilledHeadcount);
            Assert.AreEqual(0, model.PaidActiveHeadcount);
            Assert.AreEqual(0, model.WageEmploymentHeadcount);
            Assert.AreEqual(3, model.VacantPositions);
        }

        [Test]
        public void Headcount_NeverExceedsAssignedWorkers()
        {
            var filled = new WorkerSlotState();
            filled.Assign("7", "Ada", 1200);
            filled.MarkPaidActive();

            var slots = new List<WorkerSlotState>
            {
                filled,
                new WorkerSlotState("clerk", "Clerk", 1000, true),
                new WorkerSlotState("porter", "Porter", 900, true),
            };

            WorkforceStaffingReadModel model = WorkforceStaffingReadModel.FromAssignments("biz-1", slots);

            Assert.AreEqual(1, model.FilledHeadcount);
            Assert.AreEqual(1, model.PaidActiveHeadcount);
            Assert.LessOrEqual(model.FilledHeadcount, slots.Count);
        }

        [Test]
        public void SuspendedWorkers_AreAttachedButNotActiveLabor()
        {
            var suspended = new WorkerSlotState();
            suspended.Assign("8", "Bob", 1100);
            suspended.SuspendForMissedPayroll();

            WorkforceStaffingReadModel model = WorkforceStaffingReadModel.FromAssignments(
                "biz-1", new List<WorkerSlotState> { suspended });

            Assert.AreEqual(1, model.FilledHeadcount);
            Assert.AreEqual(1, model.SuspendedHeadcount);
            Assert.AreEqual(0, model.PaidActiveHeadcount);
        }

        [Test]
        public void ReadModel_DoesNotMutateSlotState()
        {
            var slot = new WorkerSlotState("clerk", "Clerk", 1000, true);
            var slots = new List<WorkerSlotState> { slot };
            string beforeId = slot.SlotId;
            int beforeWage = slot.WeeklyWageCents;

            WorkforceStaffingReadModel.FromAssignments("biz-1", slots);

            Assert.AreEqual(beforeId, slot.SlotId);
            Assert.AreEqual(beforeWage, slot.WeeklyWageCents);
            Assert.IsFalse(slot.IsFilled);
        }
    }
}
