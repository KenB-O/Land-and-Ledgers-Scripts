using System.Collections.Generic;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// PKG-7 (PL-21): WorkerSlot decomposed into RoleDefinition + PositionState +
    /// EmploymentRelationship (3A-D19), with the WorkerSlotState compatibility projection
    /// for existing consumers.
    /// </summary>
    [TestFixture]
    public sealed class WorkerSlotDecompositionTests
    {
        [Test]
        public void RoleDefinition_AdaptsWorkerSlotDefinition()
        {
            Assert.AreEqual(string.Empty, RoleDefinition.FromWorkerSlotDefinition(null).RoleId);

            // Structural: the adapter carries the authored staffing need from the definition.
            RoleDefinition adapted = RoleDefinition.FromWorkerSlotDefinition(new WorkerSlotDefinition());

            Assert.AreEqual("worker", adapted.RoleId);
            Assert.AreEqual("Worker", adapted.DisplayName);
            Assert.AreEqual(1200, adapted.BaselineWeeklyWageCents);
        }

        [Test]
        public void Position_CarriesNoWorkerIdentity_AndNoWage()
        {
            var slot = new WorkerSlotState("clerk", "Clerk", 1000, false);
            slot.Assign("7", "Ada Example", 1200);

            PositionState position = PositionState.FromWorkerSlot("biz-1", slot);

            Assert.AreEqual("biz-1:clerk", position.PositionId);
            Assert.AreEqual("biz-1", position.BusinessId);
            Assert.AreEqual("clerk", position.RoleId);
            Assert.AreEqual("Clerk", position.DisplayName);
            // The position knows the role and the business need - nothing about the worker or wage.
        }

        [Test]
        public void PositionId_IsBusinessScoped_AndDeterministic()
        {
            Assert.AreEqual("biz-1:clerk", PositionState.BuildPositionId("biz-1", "clerk"));
            Assert.AreEqual(
                PositionState.BuildPositionId("biz-1", "clerk"),
                PositionState.BuildPositionId("biz-1", "clerk"));
            Assert.AreNotEqual(
                PositionState.BuildPositionId("biz-1", "clerk"),
                PositionState.BuildPositionId("biz-2", "clerk"));
        }

        [Test]
        public void CompatibilityProjection_VacantPosition_HasNoWorker()
        {
            var position = new PositionState
            {
                PositionId = "biz-1:clerk",
                BusinessId = "biz-1",
                RoleId = "clerk",
                DisplayName = "Clerk",
                RequiredForOpening = true,
            };

            WorkerSlotState projected = WorkerSlotCompatibility.ToLegacyProjection(position, null);

            Assert.AreEqual("clerk", projected.SlotId);
            Assert.AreEqual("Clerk", projected.SlotDisplayName);
            Assert.IsTrue(projected.RequiredForOpening);
            Assert.IsFalse(projected.IsFilled);
            Assert.AreEqual(0, projected.WeeklyWageCents);
        }

        [Test]
        public void CompatibilityProjection_ActiveEmployment_RestoresLegacyShape()
        {
            var position = new PositionState
            {
                PositionId = "biz-1:clerk",
                BusinessId = "biz-1",
                RoleId = "clerk",
                DisplayName = "Clerk",
                RequiredForOpening = false,
            };
            var employment = new EmploymentRelationship
            {
                Id = "biz-1:clerk",
                EmployeePersonId = 7,
                EmployerBusinessId = "biz-1",
                Compensation = CompensationTerms.FromWeeklyWage(1200, "test"),
                LifecycleState = EmploymentLifecycleState.Active,
            };

            WorkerSlotState projected = WorkerSlotCompatibility.ToLegacyProjection(position, employment);

            Assert.AreEqual("clerk", projected.SlotId);
            Assert.AreEqual("7", projected.AssignedWorkerId);
            Assert.AreEqual(1200, projected.WeeklyWageCents);
            Assert.IsTrue(projected.IsFilled);
            Assert.IsTrue(projected.IsPaidActive);
            Assert.IsFalse(projected.SuspendedForMissedPayroll);
        }

        [Test]
        public void CompatibilityProjection_SuspendedEmployment_MapsSuspension()
        {
            var position = new PositionState
            {
                PositionId = "biz-1:clerk",
                BusinessId = "biz-1",
                RoleId = "clerk",
                DisplayName = "Clerk",
            };
            var employment = new EmploymentRelationship
            {
                Id = "biz-1:clerk",
                EmployeePersonId = 7,
                EmployerBusinessId = "biz-1",
                Compensation = CompensationTerms.FromWeeklyWage(1200, "test"),
                LifecycleState = EmploymentLifecycleState.Suspended,
                SuspensionReason = EmploymentSuspensionReason.EmployerPaymentDefault,
            };

            WorkerSlotState projected = WorkerSlotCompatibility.ToLegacyProjection(position, employment);

            Assert.IsTrue(projected.IsFilled);
            Assert.IsFalse(projected.IsPaidActive);
            Assert.IsTrue(projected.SuspendedForMissedPayroll);
        }

        [Test]
        public void FieldOwnership_CoversAllLegacyFields()
        {
            string[] legacyFields =
            {
                "slotId", "slotDisplayName", "assignedWorkerId", "assignedWorkerDisplayName",
                "weeklyWageCents", "requiredForOpening", "paidActive", "suspendedForMissedPayroll",
            };

            for (int i = 0; i < legacyFields.Length; i++)
            {
                string owner = WorkerSlotFieldOwnership.GetOwner(legacyFields[i]);
                Assert.AreNotEqual("Unknown field", owner, $"No owner documented for {legacyFields[i]}");
            }

            // Compensation ownership is the critical 3A-D21 invariant.
            Assert.IsTrue(WorkerSlotFieldOwnership.GetOwner("weeklyWageCents").Contains("EmploymentRelationship"));
        }
    }
}
