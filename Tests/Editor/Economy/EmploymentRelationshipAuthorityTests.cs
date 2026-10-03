using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// PKG-6 (PL-20): EmploymentRelationship as the sole wage-employment/compensation
    /// authority (3A-D18/D21). Projection from legacy WorkerSlot assignments, suspension
    /// mapping, and the single-source compensation read.
    /// </summary>
    [TestFixture]
    public sealed class EmploymentRelationshipAuthorityTests
    {
        private static WorkerSlotState FilledSlot(string slotId, string workerId, int wageCents)
        {
            var slot = new WorkerSlotState();
            slot.Assign(workerId, $"Worker {workerId}", wageCents);
            return slot;
        }

        [Test]
        public void Projector_SeedsCompensationFromSlotWage()
        {
            var slots = new List<WorkerSlotState> { FilledSlot("clerk", "7", 1200) };

            List<EmploymentRelationship> projected =
                EmploymentRelationshipProjector.ProjectFromWorkerSlots("biz-1", slots, startDayIndex: 42);

            Assert.AreEqual(1, projected.Count);
            EmploymentRelationship relationship = projected[0];
            Assert.AreEqual("biz-1:clerk", relationship.Id);
            Assert.AreEqual(7, relationship.EmployeePersonId);
            Assert.AreEqual("biz-1", relationship.EmployerBusinessId);
            Assert.AreEqual(1200, relationship.Compensation.AgreedWeeklyWageCents);
            Assert.AreEqual(EmploymentLifecycleState.Active, relationship.LifecycleState);
            Assert.AreEqual(EmploymentSource.ProjectedFromWorkerSlot, relationship.Source);
        }

        [Test]
        public void Projector_SuspendedForMissedPayroll_MapsToSuspended()
        {
            WorkerSlotState slot = FilledSlot("clerk", "7", 1200);
            slot.SuspendForMissedPayroll();
            var slots = new List<WorkerSlotState> { slot };

            List<EmploymentRelationship> projected =
                EmploymentRelationshipProjector.ProjectFromWorkerSlots("biz-1", slots, startDayIndex: 42);

            Assert.AreEqual(1, projected.Count);
            Assert.AreEqual(EmploymentLifecycleState.Suspended, projected[0].LifecycleState);
            Assert.AreEqual(EmploymentSuspensionReason.EmployerPaymentDefault, projected[0].SuspensionReason);
        }

        [Test]
        public void Projector_UnpaidAssignedWorker_MapsToLegacySuspended()
        {
            // Assigned but never marked paid-active: legacy unpaid attachment, not active labor.
            var unpaid = WorkerSlotState.FromSaveDto(new LandLedgers.Persistence.WorkerSlotSaveDto
            {
                slotId = "clerk",
                slotDisplayName = "Clerk",
                assignedWorkerId = "7",
                assignedWorkerDisplayName = "Worker 7",
                weeklyWageCents = 1200,
                paidActive = false,
                suspendedForMissedPayroll = false,
            });
            var slots = new List<WorkerSlotState> { unpaid };

            List<EmploymentRelationship> projected =
                EmploymentRelationshipProjector.ProjectFromWorkerSlots("biz-1", slots, startDayIndex: 42);

            Assert.AreEqual(1, projected.Count);
            Assert.AreEqual(EmploymentLifecycleState.Suspended, projected[0].LifecycleState);
            Assert.AreEqual(EmploymentSuspensionReason.LegacyUnpaid, projected[0].SuspensionReason);
        }

        [Test]
        public void Projector_SkipsVacantSlots_AndSyntheticOwners()
        {
            var slots = new List<WorkerSlotState>
            {
                new WorkerSlotState("vacant", "Vacant", 1000, false),
                FilledSlot("owner-slot", "owner:3", 0),
            };

            List<EmploymentRelationship> projected =
                EmploymentRelationshipProjector.ProjectFromWorkerSlots("biz-1", slots, startDayIndex: 42);

            Assert.AreEqual(0, projected.Count);
        }

        [Test]
        public void Registry_IsSingleSourceForAgreedCompensation()
        {
            var registry = new EmploymentRelationshipRegistry();
            var relationship = new EmploymentRelationship
            {
                Id = "biz-1:clerk",
                EmployeePersonId = 7,
                EmployerBusinessId = "biz-1",
                Compensation = CompensationTerms.FromWeeklyWage(1200, "test"),
                LifecycleState = EmploymentLifecycleState.Active,
            };

            Assert.IsNull(registry.Register(relationship));
            Assert.IsTrue(registry.GetAgreedWeeklyWageCents("biz-1:clerk", out int wageCents));
            Assert.AreEqual(1200, wageCents);
            Assert.IsFalse(registry.GetAgreedWeeklyWageCents("biz-1:missing", out _));
        }

        [Test]
        public void Registry_DuplicateId_RejectedDeterministically()
        {
            var registry = new EmploymentRelationshipRegistry();
            var first = new EmploymentRelationship { Id = "biz-1:clerk", EmployeePersonId = 7 };
            var second = new EmploymentRelationship { Id = "biz-1:clerk", EmployeePersonId = 8 };

            Assert.IsNull(registry.Register(first));
            Assert.IsNotNull(registry.Register(second));
            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGetById("biz-1:clerk", out EmploymentRelationship stored));
            Assert.AreEqual(7, stored.EmployeePersonId);
        }

        [Test]
        public void Employment_MapsToWageEmploymentTaxonomyKind()
        {
            var permanent = new EmploymentRelationship { Kind = EmploymentKind.Permanent };
            var seasonal = new EmploymentRelationship { Kind = EmploymentKind.SeasonalOrCasual };

            Assert.AreEqual(WorkRelationshipKind.PermanentEmployment, permanent.ToWorkRelationshipKind());
            Assert.AreEqual(WorkRelationshipKind.SeasonalOrCasualEmployment, seasonal.ToWorkRelationshipKind());
            Assert.IsTrue(new LandLedgers.Primitives.WorkRelationship { Kind = permanent.ToWorkRelationshipKind() }.IsWageEmployment);
        }
    }
}
