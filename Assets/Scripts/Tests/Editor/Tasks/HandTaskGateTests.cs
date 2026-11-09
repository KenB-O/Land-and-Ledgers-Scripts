// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Physical hands campaign P1c — hand gate in task start (plan section 6).
// Every test invokes the real TaskAuthority and the real EquipmentTaskGate
// with real HandOccupancyAuthority instances, not a fixture copy of the
// gate logic. Player id 101 and NPC id 202 follow the V1 / U-3 parity
// convention: the same gate class with different person ids, no
// special-case branch. The gate is check-only: starting, completing,
// interrupting, or cancelling a task never changes hand state.

using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    [TestFixture]
    public sealed class HandTaskGateTests
    {
        private const int PlayerId = 101;
        private const int NpcId = 202;

        private sealed class Fixture
        {
            public TaskAuthority Authority = new TaskAuthority();
            public List<EquipmentAsset> Assets = new List<EquipmentAsset>();
            public List<ToolKitInstance> Kits = new List<ToolKitInstance>();
            public EquipmentTaskGate Gate;
            public Dictionary<int, HandOccupancyAuthority> Hands = new Dictionary<int, HandOccupancyAuthority>();

            public Fixture()
            {
                Gate = new EquipmentTaskGate
                {
                    AssetsSupplier = () => Assets,
                    KitsSupplier = () => Kits,
                    HandOccupancySupplier = personId =>
                    {
                        HandOccupancyAuthority authority;
                        return Hands.TryGetValue(personId, out authority) ? authority.Current : null;
                    },
                };
                Authority.SetExecutionGate(Gate);
            }

            public HandOccupancyAuthority HandsFor(int personId)
            {
                var authority = new HandOccupancyAuthority(personId);
                Hands[personId] = authority;
                return authority;
            }

            public TaskDefinition Def(string definitionId, string handRequirementSpec)
            {
                var def = new TaskDefinition(definitionId, "Hand gate task", 10);
                def.SetHandRequirementSpec(handRequirementSpec);
                string ignored;
                Authority.RegisterDefinition(def, out ignored);
                return def;
            }

            public WorkTask CreateAndAssign(string definitionId, int personId)
            {
                var owner = EntityId.For(EntityKind.Business, 7);
                WorkTask task = Authority.CreateTask(definitionId, owner, 0);
                var worker = EntityId.For(EntityKind.Person, personId);
                string rejection;
                Assert.IsTrue(Authority.AssignTask(task.TaskId, worker, null, out rejection),
                    "AssignTask should succeed in fixture: " + rejection);
                return task;
            }

            public bool Start(WorkTask task, out string rejection)
            {
                return Authority.StartTask(task.TaskId, 0, out rejection);
            }
        }

        private static LotKey BreadLot() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "lot-bread-1");

        private static LotKey FlourLot() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "lot-flour-1");

        private static void OccupyLeft(HandOccupancyAuthority authority, LotKey lot, string batchId)
        {
            HandOperationResult result = authority.AllocateOneHanded(
                lot, batchId, HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(result.Succeeded, result.Reason);
        }

        private static void OccupyRight(HandOccupancyAuthority authority, LotKey lot, string batchId)
        {
            HandOperationResult result = authority.AllocateOneHanded(
                lot, batchId, HandRequirement.OneHanded, HandSlot.Right);
            Assert.IsTrue(result.Succeeded, result.Reason);
        }

        private static void OccupyBoth(HandOccupancyAuthority authority)
        {
            OccupyLeft(authority, BreadLot(), "carry-12-1");
            OccupyRight(authority, FlourLot(), "carry-12-2");
        }

        private static void AssertHandsUnchanged(HandOccupancyAuthority authority)
        {
            HandOccupancy current = authority.Current;
            Assert.IsTrue(current.LeftLotKey.Equals(BreadLot()));
            Assert.AreEqual("carry-12-1", current.LeftBatchId);
            Assert.IsTrue(current.IsRightFree);
        }

        [Test]
        public void NoRequirement_StartsWithBothHandsOccupied()
        {
            var f = new Fixture();
            f.Def("hand-none", string.Empty);
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyBoth(hands);
            WorkTask task = f.CreateAndAssign("hand-none", PlayerId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(started, "No hand requirement means the gate skips the hand check: " + rejection);
        }

        [Test]
        public void OneHanded_StartsWhenOneHandFree()
        {
            var leftOccupied = new Fixture();
            leftOccupied.Def("hand-one", "one-handed");
            HandOccupancyAuthority leftHands = leftOccupied.HandsFor(PlayerId);
            OccupyLeft(leftHands, BreadLot(), "carry-12-1");
            WorkTask leftTask = leftOccupied.CreateAndAssign("hand-one", PlayerId);
            string leftRejection;
            Assert.IsTrue(leftOccupied.Start(leftTask, out leftRejection),
                "One free hand satisfies a one-handed task: " + leftRejection);

            var rightOccupied = new Fixture();
            rightOccupied.Def("hand-one", "one-handed");
            HandOccupancyAuthority rightHands = rightOccupied.HandsFor(PlayerId);
            OccupyRight(rightHands, BreadLot(), "carry-12-1");
            WorkTask rightTask = rightOccupied.CreateAndAssign("hand-one", PlayerId);
            string rightRejection;
            Assert.IsTrue(rightOccupied.Start(rightTask, out rightRejection),
                "Mirror case, one free hand satisfies a one-handed task: " + rightRejection);
        }

        [Test]
        public void OneHanded_RefusesWhenBothOccupied_NamesMissingHand()
        {
            var f = new Fixture();
            f.Def("hand-one", "one-handed");
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyBoth(hands);
            WorkTask task = f.CreateAndAssign("hand-one", PlayerId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(!started, "Both hands occupied must refuse a one-handed task.");
            Assert.IsTrue(rejection.Contains("one free hand"), "Refusal names the missing free hand: " + rejection);
            Assert.IsTrue(rejection.Contains("lot-bread-1"), "Refusal names the left-hand lot: " + rejection);
            Assert.IsTrue(rejection.Contains("lot-flour-1"), "Refusal names the right-hand lot: " + rejection);
            Assert.IsTrue(rejection.Contains("hand-one"), "Refusal names the definition id: " + rejection);
            Assert.IsTrue(rejection.Contains("101"), "Refusal names the assignee person id: " + rejection);
        }

        [Test]
        public void TwoHanded_StartsWhenBothFree()
        {
            var f = new Fixture();
            f.Def("hand-two", "two-handed");
            f.HandsFor(PlayerId);
            WorkTask task = f.CreateAndAssign("hand-two", PlayerId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(started, "Both hands free satisfies a two-handed task: " + rejection);
        }

        [Test]
        public void TwoHanded_RefusesWhenOneOccupied_NamesOccupiedHand()
        {
            var leftOccupied = new Fixture();
            leftOccupied.Def("hand-two", "two-handed");
            HandOccupancyAuthority leftHands = leftOccupied.HandsFor(PlayerId);
            OccupyLeft(leftHands, BreadLot(), "carry-12-1");
            WorkTask leftTask = leftOccupied.CreateAndAssign("hand-two", PlayerId);
            string leftRejection;
            bool leftStarted = leftOccupied.Start(leftTask, out leftRejection);
            Assert.IsTrue(!leftStarted, "One occupied hand must refuse a two-handed task.");
            Assert.IsTrue(leftRejection.Contains("left hand"), "Refusal names the left hand: " + leftRejection);
            Assert.IsTrue(leftRejection.Contains("lot-bread-1"), "Refusal names the occupying lot: " + leftRejection);
            Assert.IsTrue(leftRejection.Contains("both hands free"), "Refusal names the two-handed need: " + leftRejection);

            var rightOccupied = new Fixture();
            rightOccupied.Def("hand-two", "two-handed");
            HandOccupancyAuthority rightHands = rightOccupied.HandsFor(PlayerId);
            OccupyRight(rightHands, FlourLot(), "carry-12-2");
            WorkTask rightTask = rightOccupied.CreateAndAssign("hand-two", PlayerId);
            string rightRejection;
            bool rightStarted = rightOccupied.Start(rightTask, out rightRejection);
            Assert.IsTrue(!rightStarted, "Mirror case, one occupied hand must refuse a two-handed task.");
            Assert.IsTrue(rightRejection.Contains("right hand"), "Refusal names the right hand: " + rightRejection);
            Assert.IsTrue(rightRejection.Contains("lot-flour-1"), "Refusal names the occupying lot: " + rightRejection);
        }

        [Test]
        public void NoSupplier_SkipsCheckAndStarts()
        {
            var f = new Fixture();
            f.Def("hand-two", "two-handed");
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyBoth(hands);
            f.Gate.HandOccupancySupplier = null;
            WorkTask task = f.CreateAndAssign("hand-two", PlayerId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(started, "Null supplier means absent, never a phantom refusal: " + rejection);
        }

        [Test]
        public void UnknownPerson_SkipsCheckAndStarts()
        {
            var f = new Fixture();
            f.Def("hand-two", "two-handed");
            f.HandsFor(PlayerId);
            WorkTask task = f.CreateAndAssign("hand-two", NpcId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(started, "Unknown person returns no snapshot, never a refusal: " + rejection);
        }

        [Test]
        public void NoAssignee_GateSkipsDirectCheck()
        {
            var f = new Fixture();
            TaskDefinition def = f.Def("hand-two", "two-handed");
            f.HandsFor(PlayerId);
            WorkTask task = f.Authority.CreateTask("hand-two", EntityId.For(EntityKind.Business, 7), 0);

            string refusal = ((ITaskExecutionGate)f.Gate).CheckStart(task, def, 0);

            Assert.IsTrue(refusal == null, "No assignee means no person whose hands could be checked: " + refusal);
        }

        [Test]
        public void RefusalNamesBothHandsAndLots()
        {
            var f = new Fixture();
            f.Def("hand-two", "two-handed");
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyBoth(hands);
            WorkTask task = f.CreateAndAssign("hand-two", PlayerId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(!started, "Both hands occupied must refuse a two-handed task.");
            Assert.IsTrue(rejection.Contains("left hand"), "Refusal names the left hand: " + rejection);
            Assert.IsTrue(rejection.Contains("right hand"), "Refusal names the right hand: " + rejection);
            Assert.IsTrue(rejection.Contains("lot-bread-1"), "Refusal names the bread lot id: " + rejection);
            Assert.IsTrue(rejection.Contains("lot-flour-1"), "Refusal names the flour lot id: " + rejection);
            Assert.IsTrue(rejection.Contains("carry-12-1"), "Refusal names the left-hand batch id: " + rejection);
            Assert.IsTrue(rejection.Contains("carry-12-2"), "Refusal names the right-hand batch id: " + rejection);
        }

        [Test]
        public void OtherPersonHands_DoNotBlock()
        {
            var f = new Fixture();
            f.Def("hand-one", "one-handed");
            HandOccupancyAuthority npcHands = f.HandsFor(NpcId);
            OccupyBoth(npcHands);
            f.HandsFor(PlayerId);
            WorkTask task = f.CreateAndAssign("hand-one", PlayerId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(started, "Another person's full hands never block the assignee: " + rejection);
        }

        [Test]
        public void Complete_ReleasesNothing_HandStateUnchanged()
        {
            var f = new Fixture();
            f.Def("hand-one", "one-handed");
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyLeft(hands, BreadLot(), "carry-12-1");
            WorkTask task = f.CreateAndAssign("hand-one", PlayerId);
            string rejection;
            Assert.IsTrue(f.Start(task, out rejection), "Start: " + rejection);

            Assert.IsTrue(f.Authority.CompleteTask(task.TaskId, TaskOutcome.Success, 0, null));
            AssertHandsUnchanged(hands);
        }

        [Test]
        public void Interrupt_ReleasesNothing_HandStateUnchanged()
        {
            var f = new Fixture();
            f.Def("hand-one", "one-handed");
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyLeft(hands, BreadLot(), "carry-12-1");
            WorkTask task = f.CreateAndAssign("hand-one", PlayerId);
            string rejection;
            Assert.IsTrue(f.Start(task, out rejection), "Start: " + rejection);

            Assert.IsTrue(f.Authority.InterruptTask(task.TaskId, null, out rejection), "Interrupt: " + rejection);
            AssertHandsUnchanged(hands);
        }

        [Test]
        public void Cancel_ReleasesNothing_HandStateUnchanged()
        {
            var f = new Fixture();
            f.Def("hand-one", "one-handed");
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyLeft(hands, BreadLot(), "carry-12-1");
            WorkTask task = f.CreateAndAssign("hand-one", PlayerId);
            string rejection;
            Assert.IsTrue(f.Start(task, out rejection), "Start: " + rejection);

            Assert.IsTrue(f.Authority.CancelTask(task.TaskId, null));
            AssertHandsUnchanged(hands);
        }

        [Test]
        public void PlayerNpcParity_SameRefusalAndStart()
        {
            foreach (int personId in new[] { PlayerId, NpcId })
            {
                var refuseFixture = new Fixture();
                refuseFixture.Def("hand-one", "one-handed");
                HandOccupancyAuthority refuseHands = refuseFixture.HandsFor(personId);
                OccupyBoth(refuseHands);
                WorkTask refuseTask = refuseFixture.CreateAndAssign("hand-one", personId);
                string refuseRejection;
                bool refuseStarted = refuseFixture.Start(refuseTask, out refuseRejection);
                Assert.IsTrue(!refuseStarted, "Person " + personId + " with both hands occupied must refuse.");
                Assert.IsTrue(refuseRejection.Contains("one free hand"),
                    "Person " + personId + " refusal names the missing free hand: " + refuseRejection);

                var startFixture = new Fixture();
                startFixture.Def("hand-one", "one-handed");
                HandOccupancyAuthority startHands = startFixture.HandsFor(personId);
                OccupyLeft(startHands, BreadLot(), "carry-12-1");
                WorkTask startTask = startFixture.CreateAndAssign("hand-one", personId);
                string startRejection;
                Assert.IsTrue(startFixture.Start(startTask, out startRejection),
                    "Person " + personId + " with one free hand must start: " + startRejection);
            }
        }

        [Test]
        public void FreightPrefix_Excluded()
        {
            var f = new Fixture();
            f.Def("freight:load:test-1", "two-handed");
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyBoth(hands);
            WorkTask task = f.CreateAndAssign("freight:load:test-1", PlayerId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(started, "Freight prefix definitions are excluded from hand gating: " + rejection);
        }

        [Test]
        public void FreightTag_Excluded()
        {
            var f = new Fixture();
            TaskDefinition def = f.Def("gs.unload-freight", "two-handed");
            def.DomainTags.Add("freight");
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyBoth(hands);
            WorkTask task = f.CreateAndAssign("gs.unload-freight", PlayerId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(started, "Freight-tagged definitions are excluded from hand gating: " + rejection);
        }

        [Test]
        public void MalformedSpec_RefusesLoudly()
        {
            var f = new Fixture();
            f.Def("hand-bad", "three-handed");
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyBoth(hands);
            WorkTask task = f.CreateAndAssign("hand-bad", PlayerId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(!started, "An unknown hand spec must refuse, never be assumed.");
            Assert.IsTrue(rejection.Contains("three-handed"), "Refusal names the unknown spec: " + rejection);
            Assert.IsTrue(rejection.Contains("hand-bad"), "Refusal names the definition id: " + rejection);
        }

        [Test]
        public void EquipmentRefusal_TakesPrecedence()
        {
            var f = new Fixture();
            TaskDefinition def = f.Def("hand-two", "two-handed");
            def.EquipmentClasses.Add(EquipmentRequirementCodes.Asset("moldboard-plow"));
            HandOccupancyAuthority hands = f.HandsFor(PlayerId);
            OccupyBoth(hands);
            WorkTask task = f.CreateAndAssign("hand-two", PlayerId);

            string rejection;
            bool started = f.Start(task, out rejection);

            Assert.IsTrue(!started, "Missing equipment must refuse.");
            Assert.IsTrue(rejection.Contains("moldboard-plow"),
                "Equipment refusal names the missing equipment: " + rejection);
            Assert.IsTrue(!rejection.Contains("free hand"),
                "Hand check never ran after the equipment refusal: " + rejection);
        }

        [Test]
        public void ProductionHandRegistry_RejectsCrossPersonDuplicateAndRoundTripsPass9()
        {
            var hands = new PersonHandOccupancyRegistry();
            hands.EnsurePerson(PlayerId);
            HandOperationResult pickup = hands.AllocateOneHanded(
                PlayerId, BreadLot(), "bread-batch-1", HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(pickup.Succeeded, pickup.Reason);

            HandOperationResult duplicate = hands.AllocateOneHanded(
                NpcId, BreadLot(), "bread-batch-1", HandRequirement.OneHanded, HandSlot.Right);
            Assert.IsFalse(duplicate.Succeeded, "A lot already in one person's custody cannot be allocated to another person.");
            Assert.IsTrue(duplicate.Reason.Contains("already held by another person"), duplicate.Reason);

            var save = new Series2Pass9SaveState { PersonHands = hands };
            Series2Pass9SaveDto dto = save.Capture(new List<string>());
            Series2Pass9SaveDto serializedRoundTrip = UnityEngine.JsonUtility.FromJson<Series2Pass9SaveDto>(
                UnityEngine.JsonUtility.ToJson(dto));
            Assert.IsNotNull(serializedRoundTrip);
            Assert.AreEqual(1, serializedRoundTrip.PersonHands.Count,
                "Unity JSON save data must retain the per-person hand section.");
            var restoredHands = new PersonHandOccupancyRegistry();
            var restored = new Series2Pass9SaveState { PersonHands = restoredHands };
            var diagnostics = new List<string>();
            restored.Restore(serializedRoundTrip, null, diagnostics);
            restored.Restore(serializedRoundTrip, null, diagnostics);

            HandOccupancy snapshot = restoredHands.SnapshotFor(PlayerId);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(BreadLot(), snapshot.LeftLotKey);
            Assert.AreEqual("bread-batch-1", snapshot.LeftBatchId);
            Assert.IsTrue(snapshot.IsRightFree);
            Assert.AreEqual(1, restoredHands.PersonCount, "Repeated restore must clear then load without duplicating people.");
        }
    }
}
