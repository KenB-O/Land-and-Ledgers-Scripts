// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Physical hands campaign P3 - individual physical capability as a safe
// integration contract (plan section 8). Every test invokes the real
// PhysicalCapacityEvaluator, the real HandOccupancyAuthority, and the real
// PersonInventoryReadModel, not fixture copies of the consult logic.
// Player id 101 and NPC id 202 follow the V1 and U-3 parity convention.
// The evaluator is constructed with null authorities, which the
// capability method needs none of, mirroring the satchel and pail leg tests.
//
// TEST-ONLY NUMBERS - every strength, body size, and fatigue value below
// is a test-only Known measure labelled as such, and carries no production
// meaning and no kilogram meaning. Test-only equipment kind strings use
// the prefix test, so no test string can be mistaken for an authored
// equipment kind. Real-person inputs are built through
// PersonCarryInputs FromPersonState with a person born 1800-01-15 read
// on 1830-06-01 for age 30, labor capacity 80, and nutrition 0.9.
//
// Covered - ordinary allocate with no consult, wrapper success with an
// empty demand one-handed and two-handed, release and satchel transfer
// after a capability allocate with no consult on those paths, real-person
// Unknown refusals naming strength, body size, and fatigue, duration and
// size refusals, the all-three MissingInputs order, the demanded-only
// diagnostic, Known comparison refusals and boundary passes, equipment
// kind held and not held, held kind giving no strength bonus, an expired
// grant not counting, null evaluator refusing loudly, a passed consult
// with an occupied hand refusing under the wrapper name, refusal
// surfacing in the read view and clearing after success, the evaluator
// still storing no capacity, player and NPC parity, no sex-based rule,
// and durability never mapped.

using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class PhysicalCapabilityTests
    {
        private const int PlayerId = 101;
        private const int NpcId = 202;
        private const int DayIndex = 12;

        private static LotKey BreadLot() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "lot-bread-1");

        private static LotKey FlourLot() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "lot-flour-1");

        private static LotKey ToolLot() =>
            new LotKey(CustodyTransferLog.BusinessAuthority("blacksmith-1"), "lot-hammer-1");

        private static LotKey TestItemLot() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "lot-test-item-1");

        private static PhysicalCapacityEvaluator Evaluator() =>
            new PhysicalCapacityEvaluator(null, null, null, null);

        private static PersonState RealPerson(int personId)
        {
            var person = new PersonState
            {
                id = personId,
                firstName = "Test",
                lastName = "Person",
                age = 999,
                birthDate = new BirthDate(1800, 1, 15),
            };
            person.health.laborCapacityPercent = 80;
            person.nutrition.nutrition01 = 0.9f;
            return person;
        }

        private static PersonCarryInputs RealInputs(PersonState person) =>
            PersonCarryInputs.FromPersonState(person, 1830, 6, 1, new string[0]);

        private static PersonCarryInputs KnownInputs(
            PhysicalMeasure strength, PhysicalMeasure bodySize, PhysicalMeasure fatigue,
            IEnumerable<string> held) =>
            new PersonCarryInputs(PlayerId, 30, 80, 0.9f, fatigue, bodySize, strength, held);

        private static PhysicalCapabilityDemand LiftDemand(float minimum) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Known(minimum), PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown, 0, false, string.Empty);

        private static PhysicalCapabilityDemand CarryDemand(float minimum) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Unknown, PhysicalMeasure.Known(minimum), PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown, 0, false, string.Empty);

        private static PhysicalCapabilityDemand BodySizeDemand(float minimum) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Known(minimum),
                PhysicalMeasure.Unknown, 0, false, string.Empty);

        private static PhysicalCapabilityDemand FatigueDemand(float maximum) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                PhysicalMeasure.Known(maximum), 0, false, string.Empty);

        private static PhysicalCapabilityDemand LiftAndCarryDemand(float lift, float carry) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Known(lift), PhysicalMeasure.Known(carry), PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown, 0, false, string.Empty);

        private static PhysicalCapabilityDemand LiftAndBodySizeDemand(float lift, float bodySize) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Known(lift), PhysicalMeasure.Unknown, PhysicalMeasure.Known(bodySize),
                PhysicalMeasure.Unknown, 0, false, string.Empty);

        private static PhysicalCapabilityDemand LiftAndFatigueDemand(float lift, float fatigue) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Known(lift), PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                PhysicalMeasure.Known(fatigue), 0, false, string.Empty);

        private static PhysicalCapabilityDemand LiftBodyFatigueDemand(float lift, float bodySize, float fatigue) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Known(lift), PhysicalMeasure.Unknown, PhysicalMeasure.Known(bodySize),
                PhysicalMeasure.Known(fatigue), 0, false, string.Empty);

        private static PhysicalCapabilityDemand BodySizeAndFatigueDemand(float bodySize, float fatigue) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Known(bodySize),
                PhysicalMeasure.Known(fatigue), 0, false, string.Empty);

        private static PhysicalCapabilityDemand DurationDemand(int minutes) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown, minutes, false, string.Empty);

        private static PhysicalCapabilityDemand SizeDemand() =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown, 0, true, string.Empty);

        private static PhysicalCapabilityDemand EquipmentDemand(string kind) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown, 0, false, kind);

        private static PhysicalCapabilityDemand EquipmentAndLiftDemand(string kind, float lift) =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Known(lift), PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown, 0, false, kind);

        private static HandCustodyContext Context(string itemId, int units, string causeId) =>
            new HandCustodyContext(DayIndex, itemId, units, causeId);

        private static WornContainerCapacity StandardCapacity() =>
            new WornContainerCapacity(
                10, PhysicalMeasure.Known(5f), PhysicalMeasure.Known(20f),
                PhysicalDimensions.Known(0.5f, 0.4f, 0.3f));

        private static WornContainer WornSatchel(int personId, string containerId, WornContainerCapacity capacity)
        {
            var satchel = new WornContainer(containerId, WornContainerKind.Satchel, capacity);
            WornContainerOperationResult worn = satchel.Wear(personId);
            Assert.IsTrue(worn.Succeeded, worn.Reason);
            return satchel;
        }

        private static void RegisterTestSpecs()
        {
            Assert.IsNull(PhysicalItemSpecRegistry.Register(new PhysicalItemSpec(
                "test-capability-item", "Test Capability Item", "piece",
                PhysicalMeasure.Known(0.5f), PhysicalMeasure.Known(1.0f),
                PhysicalDimensions.Known(0.2f, 0.15f, 0.1f),
                true, true, string.Empty)));
        }

        private sealed class Driver
        {
            public HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public Dictionary<int, HandOccupancyAuthority> Hands = new Dictionary<int, HandOccupancyAuthority>();

            public HandOccupancyAuthority AuthorityFor(int personId)
            {
                if (!Hands.TryGetValue(personId, out HandOccupancyAuthority authority))
                {
                    authority = new HandOccupancyAuthority(personId);
                    Hands[personId] = authority;
                }

                return authority;
            }

            private HandOccupancy HandSupplier(int personId)
            {
                if (Hands.TryGetValue(personId, out HandOccupancyAuthority authority))
                {
                    return authority.Current;
                }

                return null;
            }

            public PersonInventoryReadModel ReadModel()
            {
                var tracker = new PersonScheduleTracker();
                var loop = new HouseholdShoppingLoop(
                    new PopulationState(), new HouseholdMembershipRegistry(),
                    new HouseholdLedgerRegistry(), Inventories,
                    new HouseholdNeedRegistry(), new SupplierDirectory(),
                    new JourneyModel(), new TaskAuthority(),
                    new WorkTimeBudgetStore(), tracker,
                    new ShopperSellerKnowledge());
                return new PersonInventoryReadModel(
                    new HouseholdShoppingReadModel(loop, tracker),
                    Inventories, null, null,
                    HandSupplier, null);
            }
        }

        [Test]
        public void OrdinaryAllocateNeedsNoCapabilityVerdict()
        {
            var authority = new HandOccupancyAuthority(PlayerId);

            HandOperationResult result = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(BreadLot()));
            Assert.IsTrue(authority.IsFree(HandSlot.Right));
        }

        [Test]
        public void WrapperWithEmptyDemandSucceedsOneHanded()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, PhysicalCapabilityDemand.None);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, PhysicalCapabilityDemand.None);

            Assert.AreEqual(PhysicalCapacityOutcome.Fits, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(BreadLot()));
            Assert.IsTrue(authority.IsFree(HandSlot.Right));
        }

        [Test]
        public void WrapperWithEmptyDemandSucceedsTwoHanded()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, PhysicalCapabilityDemand.None);
            HandOperationResult result = authority.AllocateTwoHandedWithCapability(
                ToolLot(), "carry-12-3", HandRequirement.TwoHanded,
                evaluator, inputs, PhysicalCapabilityDemand.None);

            Assert.AreEqual(PhysicalCapacityOutcome.Fits, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(ToolLot()));
            Assert.IsTrue(authority.Current.RightLotKey.Equals(ToolLot()));
            Assert.IsTrue(authority.Current.IsTwoHandedHold);
        }

        [Test]
        public void ReleaseAfterCapabilityAllocateNeedsNoConsult()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));

            HandOperationResult held = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, PhysicalCapabilityDemand.None);
            Assert.IsTrue(held.Succeeded, held.Reason);

            HandOperationResult result = authority.Release(HandSlot.Left);

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
            Assert.AreEqual(string.Empty, authority.Current.LastRefusalReason);
            Assert.AreEqual(string.Empty, authority.Current.LastRefusalOperation);
        }

        [Test]
        public void TransferAfterCapabilityAllocateNeedsNoConsult()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());

            HandOperationResult held = authority.AllocateOneHandedWithCapability(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, PhysicalCapabilityDemand.None,
                Context("test-capability-item", 1, "capability-test-5"));
            Assert.IsTrue(held.Succeeded, held.Reason);

            HandOperationResult result = authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-capability-item", 1, "capability-test-5b"));

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
            Assert.IsTrue(satchel.Contains(TestItemLot()));
            Assert.AreEqual(1, satchel.Entries.Count);
        }

        [Test]
        public void RealPersonLiftDemandedRefusesUnknownNamingStrength()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));
            PhysicalCapabilityDemand demand = LiftDemand(5f);

            Assert.AreEqual(30, inputs.AgeYears);
            Assert.AreEqual(80, inputs.LaborCapacityPercent);
            Assert.AreEqual(0.9f, inputs.Nutrition01, 0.001f);
            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, verdict.Outcome, verdict.Reason);
            CollectionAssert.AreEqual(new[] { "strength" }, verdict.MissingInputs);
            Assert.IsTrue(verdict.Reason.Contains("strength"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Reason.Contains("strength"), result.Reason);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void RealPersonCarryDemandedRefusesUnknownNamingStrength()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));
            PhysicalCapabilityDemand demand = CarryDemand(5f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, verdict.Outcome, verdict.Reason);
            CollectionAssert.AreEqual(new[] { "strength" }, verdict.MissingInputs);
            Assert.IsTrue(verdict.Reason.Contains("strength"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void RealPersonBodySizeDemandedRefusesUnknownNamingBodySize()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));
            PhysicalCapabilityDemand demand = BodySizeDemand(4f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, verdict.Outcome, verdict.Reason);
            CollectionAssert.AreEqual(new[] { "body size" }, verdict.MissingInputs);
            Assert.IsTrue(verdict.Reason.Contains("body size"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void RealPersonFatigueDemandedRefusesUnknownNamingFatigue()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));
            PhysicalCapabilityDemand demand = FatigueDemand(5f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, verdict.Outcome, verdict.Reason);
            CollectionAssert.AreEqual(new[] { "fatigue" }, verdict.MissingInputs);
            Assert.IsTrue(verdict.Reason.Contains("fatigue"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void DurationDemandedRefusesUnknownEvenWithKnownFatigue()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Known(2f),
                new string[0]);
            PhysicalCapabilityDemand demand = DurationDemand(30);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, verdict.Outcome, verdict.Reason);
            CollectionAssert.AreEqual(new[] { "carry duration" }, verdict.MissingInputs);
            Assert.IsTrue(verdict.Reason.Contains("carry duration"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void SizeAndHandlingDemandedRefusesUnknown()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Known(10f), PhysicalMeasure.Known(10f), PhysicalMeasure.Unknown,
                new string[0]);
            PhysicalCapabilityDemand demand = SizeDemand();

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, verdict.Outcome, verdict.Reason);
            CollectionAssert.AreEqual(new[] { "object size and handling" }, verdict.MissingInputs);
            Assert.IsTrue(verdict.Reason.Contains("object size and handling"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void AllThreeDemandedReasonNamesAllThreeInOrder()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));
            PhysicalCapabilityDemand demand = LiftBodyFatigueDemand(5f, 4f, 5f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, verdict.Outcome, verdict.Reason);
            CollectionAssert.AreEqual(new[] { "body size", "strength", "fatigue" }, verdict.MissingInputs);
            Assert.IsTrue(verdict.Reason.Contains("body size"), verdict.Reason);
            Assert.IsTrue(verdict.Reason.Contains("strength"), verdict.Reason);
            Assert.IsTrue(verdict.Reason.Contains("fatigue"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void DemandedOnlyDiagnostic()
        {
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));
            PhysicalCapabilityDemand demand = LiftDemand(5f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, verdict.Outcome, verdict.Reason);
            CollectionAssert.AreEqual(new[] { "strength" }, verdict.MissingInputs);
            Assert.IsTrue(!verdict.Reason.Contains("body size"), verdict.Reason);
            Assert.IsTrue(!verdict.Reason.Contains("fatigue"), verdict.Reason);
        }

        [Test]
        public void KnownStrengthBelowLiftMinimumRefusesDoesNotFit()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Known(3f), PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                new string[0]);
            PhysicalCapabilityDemand demand = LiftDemand(5f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(verdict.Reason.Contains("lift"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void KnownStrengthMeetsLiftMinimumAllocates()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Known(5f), PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                new string[0]);
            PhysicalCapabilityDemand demand = LiftDemand(5f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Fits, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(BreadLot()));
        }

        [Test]
        public void LiftMetButCarryNotMetRefusesDoesNotFit()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Known(5f), PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                new string[0]);
            PhysicalCapabilityDemand demand = LiftAndCarryDemand(5f, 8f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(verdict.Reason.Contains("carry"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void KnownBodySizeBelowMinimumRefusesDoesNotFit()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Known(5f), PhysicalMeasure.Known(2f), PhysicalMeasure.Unknown,
                new string[0]);
            PhysicalCapabilityDemand demand = LiftAndBodySizeDemand(5f, 4f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(verdict.Reason.Contains("body size"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void KnownFatigueAboveMaximumRefusesDoesNotFit()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Known(5f), PhysicalMeasure.Unknown, PhysicalMeasure.Known(9f),
                new string[0]);
            PhysicalCapabilityDemand demand = LiftAndFatigueDemand(5f, 5f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(verdict.Reason.Contains("fatigue"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void BoundaryValuesPass()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Unknown, PhysicalMeasure.Known(4f), PhysicalMeasure.Known(5f),
                new string[0]);
            PhysicalCapabilityDemand demand = BodySizeAndFatigueDemand(4f, 5f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Fits, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(BreadLot()));
        }

        [Test]
        public void RequiredKindHeldPasses()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                new[] { "test-kind" });
            PhysicalCapabilityDemand demand = EquipmentDemand("test-kind");

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.Fits, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(BreadLot()));
        }

        [Test]
        public void RequiredKindNotHeldRefusesDoesNotFit()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                new string[0]);
            PhysicalCapabilityDemand demand = EquipmentDemand("test-kind");

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(verdict.Reason.Contains("test-kind"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void HeldKindGivesNoStrengthBonus()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Known(3f), PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                new[] { "test-kind" });
            PhysicalCapabilityDemand demand = EquipmentAndLiftDemand("test-kind", 5f);

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(verdict.Reason.Contains("lift"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void ExpiredGrantKindNotCounted()
        {
            var resolver = new EquipmentAccessResolver();
            resolver.AddGrant(new EquipmentAccessGrant
            {
                GrantId = "grant-expired-1",
                EquipmentKind = "test-kind",
                HolderKind = "person",
                HolderId = "7",
                Custody = EquipmentCustodyKind.Borrowed,
                GranterName = "Neighbour",
                ExpiryDayIndex = 5,
            });
            IReadOnlyList<string> held = PersonCarryInputs.ResolveEquipmentHeld(
                resolver, "person", "7",
                new[] { "test-kind" },
                (holderKind, holderId) => new List<string>(),
                10);
            Assert.AreEqual(0, held.Count);

            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = KnownInputs(
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Unknown,
                held);
            PhysicalCapabilityDemand demand = EquipmentDemand("test-kind");

            PhysicalCapabilityVerdict verdict = evaluator.EvaluateCapability(inputs, demand);
            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);

            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(verdict.Reason.Contains("test-kind"), verdict.Reason);
            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void NullEvaluatorRefusesLoudly()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));

            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                null, inputs, PhysicalCapabilityDemand.None);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Reason.Contains("capability evaluator"), result.Reason);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void ConsultPassesButHandOccupiedRefusesUnderWrapperName()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);

            HandOperationResult result = authority.AllocateOneHandedWithCapability(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, PhysicalCapabilityDemand.None);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Reason.Contains("lot-bread-1"), result.Reason);
            Assert.AreEqual("AllocateOneHandedWithCapability", authority.Current.LastRefusalOperation);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(BreadLot()));
        }

        [Test]
        public void RefusalSurfacesInReadViewAndClearsAfterSuccess()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            var evaluator = Evaluator();
            PersonCarryInputs inputs = RealInputs(RealPerson(PlayerId));
            PhysicalCapabilityDemand demand = LiftDemand(5f);

            HandOperationResult refused = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, demand);
            Assert.IsTrue(!refused.Succeeded);

            PersonInventoryReadModel.HandOccupancyView refusedView = driver.ReadModel().HandOccupancy(PlayerId);
            Assert.IsTrue(refusedView.HasRefusal);
            Assert.AreEqual("AllocateOneHandedWithCapability", refusedView.LastRefusalOperation);
            Assert.IsTrue(refusedView.LastRefusalReason.Contains("strength"), refusedView.LastRefusalReason);

            HandOperationResult held = authority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, inputs, PhysicalCapabilityDemand.None);
            Assert.IsTrue(held.Succeeded, held.Reason);

            PersonInventoryReadModel.HandOccupancyView clearedView = driver.ReadModel().HandOccupancy(PlayerId);
            Assert.IsTrue(!clearedView.HasRefusal);
            Assert.AreEqual(string.Empty, clearedView.LastRefusalReason);
        }

        [Test]
        public void EvaluatorStillStoresNoCapacity()
        {
            FieldInfo[] fields = typeof(PhysicalCapacityEvaluator).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.AreEqual(4, fields.Length, "Evaluator holds exactly the four authority references.");
            foreach (FieldInfo field in fields)
            {
                Assert.IsTrue(field.FieldType.IsInterface || typeof(Delegate).IsAssignableFrom(field.FieldType),
                    "Evaluator field " + field.Name + " must be an authority reference, not a capacity value.");
            }
        }

        [Test]
        public void PlayerNpcParity()
        {
            var evaluator = Evaluator();
            PhysicalCapabilityDemand demand = LiftDemand(5f);

            PersonCarryInputs playerInputs = RealInputs(RealPerson(PlayerId));
            PersonCarryInputs npcInputs = RealInputs(RealPerson(NpcId));
            PhysicalCapabilityVerdict playerVerdict = evaluator.EvaluateCapability(playerInputs, demand);
            PhysicalCapabilityVerdict npcVerdict = evaluator.EvaluateCapability(npcInputs, demand);
            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, playerVerdict.Outcome, playerVerdict.Reason);
            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, npcVerdict.Outcome, npcVerdict.Reason);
            CollectionAssert.AreEqual(playerVerdict.MissingInputs, npcVerdict.MissingInputs);

            var player = new HandOccupancyAuthority(PlayerId);
            var npc = new HandOccupancyAuthority(NpcId);
            HandOperationResult playerHeld = player.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, playerInputs, PhysicalCapabilityDemand.None);
            HandOperationResult npcHeld = npc.AllocateOneHandedWithCapability(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, npcInputs, PhysicalCapabilityDemand.None);
            Assert.IsTrue(playerHeld.Succeeded, playerHeld.Reason);
            Assert.IsTrue(npcHeld.Succeeded, npcHeld.Reason);
            Assert.IsTrue(player.Current.LeftLotKey.Equals(BreadLot()));
            Assert.IsTrue(npc.Current.LeftLotKey.Equals(FlourLot()));
        }

        [Test]
        public void NoSexBasedRule()
        {
            PersonState female = RealPerson(PlayerId);
            female.isFemale = true;
            PersonState male = RealPerson(PlayerId);
            male.isFemale = false;

            var evaluator = Evaluator();
            PhysicalCapabilityDemand demand = LiftDemand(5f);
            PhysicalCapabilityVerdict femaleVerdict = evaluator.EvaluateCapability(RealInputs(female), demand);
            PhysicalCapabilityVerdict maleVerdict = evaluator.EvaluateCapability(RealInputs(male), demand);
            Assert.AreEqual(femaleVerdict.Outcome, maleVerdict.Outcome);
            CollectionAssert.AreEqual(femaleVerdict.MissingInputs, maleVerdict.MissingInputs);
            Assert.AreEqual(femaleVerdict.Reason, maleVerdict.Reason);

            var femaleAuthority = new HandOccupancyAuthority(PlayerId);
            var maleAuthority = new HandOccupancyAuthority(PlayerId);
            HandOperationResult femaleResult = femaleAuthority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, RealInputs(female), demand);
            HandOperationResult maleResult = maleAuthority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, RealInputs(male), demand);
            Assert.AreEqual(femaleResult.Succeeded, maleResult.Succeeded);
            Assert.AreEqual(femaleResult.Reason, maleResult.Reason);

            foreach (MemberInfo member in typeof(PhysicalCapabilityDemand).GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                string lower = member.Name.ToLowerInvariant();
                Assert.IsTrue(!lower.Contains("female") && !lower.Contains("sex") && !lower.Contains("gender"),
                    "PhysicalCapabilityDemand must not carry a demographic member - " + member.Name);
            }

            foreach (MemberInfo member in typeof(PhysicalCapabilityVerdict).GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                string lower = member.Name.ToLowerInvariant();
                Assert.IsTrue(!lower.Contains("female") && !lower.Contains("sex") && !lower.Contains("gender"),
                    "PhysicalCapabilityVerdict must not carry a demographic member - " + member.Name);
            }
        }

        [Test]
        public void DurabilityNotMapped()
        {
            PersonState low = RealPerson(PlayerId);
            low.hiddenWorkerTraits.durability = 0;
            PersonState high = RealPerson(PlayerId);
            high.hiddenWorkerTraits.durability = 100;

            var evaluator = Evaluator();
            PhysicalCapabilityDemand demand = LiftDemand(5f);
            PhysicalCapabilityVerdict lowVerdict = evaluator.EvaluateCapability(RealInputs(low), demand);
            PhysicalCapabilityVerdict highVerdict = evaluator.EvaluateCapability(RealInputs(high), demand);
            Assert.AreEqual(lowVerdict.Outcome, highVerdict.Outcome);
            CollectionAssert.AreEqual(lowVerdict.MissingInputs, highVerdict.MissingInputs);
            Assert.AreEqual(lowVerdict.Reason, highVerdict.Reason);

            var lowAuthority = new HandOccupancyAuthority(PlayerId);
            var highAuthority = new HandOccupancyAuthority(PlayerId);
            HandOperationResult lowResult = lowAuthority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, RealInputs(low), demand);
            HandOperationResult highResult = highAuthority.AllocateOneHandedWithCapability(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                evaluator, RealInputs(high), demand);
            Assert.AreEqual(lowResult.Succeeded, highResult.Succeeded);
            Assert.AreEqual(lowResult.Reason, highResult.Reason);
        }
    }
}
