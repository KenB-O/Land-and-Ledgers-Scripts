// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Pass IV (Worker 3) — Slice C tests: farm plowing.
//
// Real reconciliation figures: acres plowed, task minutes worked, plow
// condition (wear), team/driver reservations held and released.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Crops;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class PlowWorkServiceTests
    {
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private DraftPowerService draftPower;
        private AnimalRegistry animalRegistry;
        private FarmPlowService service;
        private PlowWorkHooks hooks;
        private List<EntityId> horses;
        private List<EntityId> drivers;
        private EquipmentAsset plow;
        private EquipmentAsset harness;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
            draftPower = new DraftPowerService();
            animalRegistry = new AnimalRegistry(new EntityIdRegistry());
            service = new FarmPlowService();

            horses = new List<EntityId>();
            for (int i = 0; i < 4; i++)
            {
                AnimalState horse = animalRegistry.RegisterAnimal(AnimalSpecies.Horse, "draft", AnimalSex.Male,
                    AnimalOwnerKind.Household, "hh-1", 42, "test team");
                Assert.IsNotNull(horse, "horse registered");
                horses.Add(horse.AnimalId);
            }
            drivers = new List<EntityId>
            {
                EntityId.For(EntityKind.Person, 501),
                EntityId.For(EntityKind.Person, 502),
            };
            plow = new EquipmentAsset { AssetId = "plow-1", Kind = "plow", DisplayName = "Walking plow", Condition01 = 0.9f };
            harness = new EquipmentAsset { AssetId = "harness-1", Kind = "harness", DisplayName = "Team harness", Condition01 = 0.9f };

            hooks = new PlowWorkHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DraftPower = draftPower,
                AnimalRegistry = animalRegistry,
                AvailableHorses = horses,
                AvailableDrivers = drivers,
                Plows = new List<EquipmentAsset> { plow },
                Harness = harness,
                DayIndex = 42,
                NowGameSeconds = () => 0f,
                Diagnostics = new List<string>(),
            };
        }

        private CropFieldState NewField(string fieldId, float acres)
        {
            var field = new CropFieldState { FieldId = fieldId, FarmId = "farm-1", Acres = acres };
            service.RegisterField(field);
            return field;
        }

        private void WorkOrderToCompletion(PlowWorkOrder order)
        {
            int guard = 0;
            while (order.Status == PlowOrderStatus.InProgress && guard++ < 30)
            {
                Assert.IsTrue(service.WorkOnPlowing(order, 60, hooks, out string reason),
                    "WorkOnPlowing failed: " + reason);
            }
            Assert.AreEqual(PlowOrderStatus.Done, order.Status, "order did not complete");
        }

        [Test]
        public void FullPlowing_ReservesEverything_ReleasesOnCompletion()
        {
            CropFieldState field = NewField("north-40", 10f);
            PlowWorkOrder order = service.RequestPlowing(field, out string reason);
            Assert.IsNotNull(order, "order queued: " + reason);
            service.Tick(hooks);

            Assert.AreEqual(PlowOrderStatus.InProgress, order.Status, "started: " + order.WaitReason);
            Assert.AreEqual("plow-1", order.AssignedPlowAssetId, "the real plow assigned");
            Assert.AreEqual(2, order.TeamAnimalIds.Count, "team of 2 horses");
            Assert.IsTrue(order.DriverPersonId.IsValid, "a real driver assigned");
            Assert.IsTrue(order.TaskId.IsValid, "a real WorkTask created");
            Assert.IsTrue(plow.IsReserved, "plow exclusively reserved");
            Assert.IsTrue(draftPower.IsDriverReserved(order.DriverPersonId), "driver reserved");
            foreach (EntityId horse in order.TeamAnimalIds)
                Assert.IsTrue(draftPower.IsAnimalReserved(horse), "horse " + horse + " reserved");
            Assert.AreEqual("plowing", field.CurrentOperation, "field marked plowing");

            // 10 acres x 30 min/acre = 300 min of work.
            WorkTask task = tasks.FindTask(order.TaskId);
            Assert.AreEqual(300, task.PlannedMinutes, "task scaled by acres");
            WorkOrderToCompletion(order);

            Assert.AreEqual(10f, order.AcresPlowed, 0.001f, "10 acres plowed");
            Assert.AreEqual(1f, field.WorkProgress01, 0.001f, "field progress complete");
            Assert.AreEqual(string.Empty, field.CurrentOperation, "field ready for planting");
            Assert.IsFalse(plow.IsReserved, "plow released");
            Assert.IsFalse(draftPower.IsDriverReserved(order.DriverPersonId), "driver released");
            foreach (EntityId horse in order.TeamAnimalIds)
                Assert.IsFalse(draftPower.IsAnimalReserved(horse), "horse " + horse + " released");
            Assert.Less(plow.Condition01, 0.9f, "plow wore with use (now " + plow.Condition01.ToString("0.00") + ")");
        }

        [Test]
        public void TwoOrders_OnePlow_SecondWaits_NotFailed()
        {
            CropFieldState fieldA = NewField("north-40", 4f);
            CropFieldState fieldB = NewField("south-40", 4f);
            PlowWorkOrder orderA = service.RequestPlowing(fieldA, out _);
            PlowWorkOrder orderB = service.RequestPlowing(fieldB, out _);
            service.Tick(hooks);

            Assert.AreEqual(PlowOrderStatus.InProgress, orderA.Status, "first order started");
            Assert.AreEqual(PlowOrderStatus.Queued, orderB.Status, "second order waits — not failed, no phantom plow");
            StringAssert.Contains("in use", orderB.WaitReason.ToLowerInvariant(), "wait reason honest: " + orderB.WaitReason);

            // Finish A; B must then start with the same real plow.
            WorkOrderToCompletion(orderA);
            service.Tick(hooks);
            Assert.AreEqual(PlowOrderStatus.InProgress, orderB.Status, "second order starts when the plow frees");
            Assert.AreEqual("plow-1", orderB.AssignedPlowAssetId, "same real plow, sequentially");
            WorkOrderToCompletion(orderB);
            Assert.AreEqual(PlowOrderStatus.Done, orderB.Status);
            Assert.AreEqual(4f, fieldB.WorkProgress01 * 4f, 0.001f, "field B plowed");
        }

        [Test]
        public void BrokenPlow_BlocksOrder_RepairUnblocks()
        {
            plow.ApplyWear(1f); // wrecked: condition 0
            Assert.IsFalse(plow.IsUsable, "plow wrecked");
            CropFieldState field = NewField("north-40", 4f);
            PlowWorkOrder order = service.RequestPlowing(field, out _);
            service.Tick(hooks);

            Assert.AreEqual(PlowOrderStatus.Blocked, order.Status, "blocked — no usable plow");
            StringAssert.Contains("repair", order.BlockedReason.ToLowerInvariant(), "reason says repair: " + order.BlockedReason);
            Assert.IsFalse(order.TaskId.IsValid, "no task conjured for a phantom plow");

            // Repair the plow: a fresh order proceeds.
            plow.RepairTo(0.8f);
            plow.RecordMaintenance("sharpened share", 42);
            PlowWorkOrder order2 = service.RequestPlowing(NewField("south-40", 2f), out _);
            service.Tick(hooks);
            Assert.AreEqual(PlowOrderStatus.InProgress, order2.Status, "repaired plow works");
        }

        [Test]
        public void BrokenPreferredPlow_UsesLegitimateAlternative()
        {
            var plow2 = new EquipmentAsset { AssetId = "plow-2", Kind = "plow", DisplayName = "Spare plow", Condition01 = 0.85f };
            hooks.Plows.Add(plow2);
            plow.ApplyWear(1f); // preferred plow wrecked
            CropFieldState field = NewField("north-40", 4f);
            PlowWorkOrder order = service.RequestPlowing(field, out _, preferredPlowAssetId: "plow-1");
            service.Tick(hooks);

            Assert.AreEqual(PlowOrderStatus.InProgress, order.Status, "order proceeds with the spare");
            Assert.AreEqual("plow-2", order.AssignedPlowAssetId, "legitimate alternative used — not the wrecked preferred plow");
            WorkOrderToCompletion(order);
            Assert.AreEqual(PlowOrderStatus.Done, order.Status);
        }

        [Test]
        public void NoTeamAvailable_Waits_NotFailed()
        {
            hooks.AvailableHorses = new List<EntityId>(); // no horses at all
            CropFieldState field = NewField("north-40", 4f);
            PlowWorkOrder order = service.RequestPlowing(field, out _);
            service.Tick(hooks);

            Assert.AreEqual(PlowOrderStatus.Queued, order.Status, "waits for a team — not failed");
            Assert.IsFalse(plow.IsReserved, "plow NOT reserved when the unit cannot assemble");
            Assert.IsFalse(order.TaskId.IsValid, "no task without a team");
        }

        [Test]
        public void DuplicateOrder_ForSameField_Refused()
        {
            CropFieldState field = NewField("north-40", 4f);
            Assert.IsNotNull(service.RequestPlowing(field, out _));
            PlowWorkOrder dup = service.RequestPlowing(field, out string reason);
            Assert.IsNull(dup, "duplicate refused");
            StringAssert.Contains("already", reason.ToLowerInvariant(), "reason: " + reason);
        }

        [Test]
        public void ZeroAcreField_Refused()
        {
            var field = new CropFieldState { FieldId = "empty", Acres = 0f };
            Assert.IsNull(service.RequestPlowing(field, out string reason));
            StringAssert.Contains("no area", reason.ToLowerInvariant(), "reason: " + reason);
        }
    }
}
