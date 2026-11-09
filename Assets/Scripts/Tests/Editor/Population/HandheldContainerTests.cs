// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Physical hands campaign P2b — one handheld container kind, a pail (plan
// section 8). Every test invokes the real HandheldContainer, the real
// HandOccupancyAuthority, the real PersonInventoryReadModel, the real
// PhysicalCapacityEvaluator, and the real CustodyTransferLog, not fixture
// copies. Player id 101 and NPC id 202 follow the V1 / U-3 parity
// convention: the same container class with different person ids, no
// special-case branch.
//
// TEST-ONLY FIXTURES: every capacity number and every test item spec
// number below is a test-only fixture, labelled as such, and is NOT an
// authored container measurement and NOT an authored item measurement.
// The test item ids ("test-pail-item", "test-pail-long-item") are
// not household catalog ids, so the catalog label check does not apply.
// The bread lot uses the seeded spec, which is Unknown on mass, volume,
// and dimensions — that Unknown is the point of the UnknownItemMass test.
// Pail ids ("pail-1") and satchel ids ("satchel-1") are kept in separate
// id spaces, because both kinds build own keys from the same
// ContainerAuthority word and a colliding id would collide on own keys.
//
// Covered: one-handed and two-handed pick up; pick up requiring a hand
// requirement; pick up into an occupied hand refused; pick up by another
// person refused naming the holder; idempotent same-hand pick up;
// other-hand pick up refused naming the current hand; pail in one hand
// plus a lot in the other; place into a held pail and into a set-down
// pail; retrieval back into the hand, from a set-down pail, and
// two-handed; retrieval into an occupied hand refused; retrieval of a
// lot not held refused; retrieval into the container hand refused; set
// down keeping contents inside and discoverable by placement location; pick up
// again with contents intact; move between hands with contents intact;
// move to an occupied hand, same-hand move, and two-handed move refused;
// two-handed pail blocking a second action; over-capacity refusals on
// the units, mass, volume, and dimensions axes; Unknown capacity
// refusing naming the missing axis; Unknown bread mass refusing naming
// bread; circular self-nesting refused; duplicate placement refused;
// container-as-contents transfer into a satchel refused; transfer into
// another person's pail refused; repeated transfer preserving totals;
// log records for pick up, place, retrieve, and set down; Fresh
// condition preserved through a move; the read model listing held
// contents, the no-supplier empty answer, the set-down invisibility,
// the unknown-person empty answer, and the hand view naming a container
// hold; player and NPC parity.

using System.Collections.Generic;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.Persistence;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class HandheldContainerTests
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

        private static LotKey TestItemLot2() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "lot-test-item-2");

        private static LotKey PailOwnKey(string containerId) =>
            new LotKey(CustodyTransferLog.ContainerAuthority, containerId);

        private static HandCustodyContext Context(string itemId, int units, string causeId) =>
            new HandCustodyContext(DayIndex, itemId, units, causeId);

        private static PhysicalCapacityEvaluator Evaluator() =>
            new PhysicalCapacityEvaluator(null, null, null, null);

        /// <summary>
        /// Test-only capacity fixture: fully Known on all four axes. The
        /// numbers carry no production meaning and are not authored
        /// container measurements.
        /// </summary>
        private static WornContainerCapacity KnownCapacity(
            int maxUnits, float maxMassKg, float maxVolumeLiters,
            float length, float width, float height) =>
            new WornContainerCapacity(
                maxUnits, PhysicalMeasure.Known(maxMassKg), PhysicalMeasure.Known(maxVolumeLiters),
                PhysicalDimensions.Known(length, width, height));

        /// <summary>
        /// Test-only standard capacity fixture: 10 units, 5 kg, 20 liters,
        /// interior 0.5 by 0.4 by 0.3 m. Test-only, not an authored
        /// container measurement. Reuses the P2a standard capacity numbers.
        /// </summary>
        private static WornContainerCapacity StandardCapacity() =>
            KnownCapacity(10, 5f, 20f, 0.5f, 0.4f, 0.3f);

        private static HandheldContainer HeldPail(
            HandOccupancyAuthority authority, int personId, string containerId,
            WornContainerCapacity capacity, HandRequirement requirement, HandSlot hand,
            string causeId)
        {
            var pail = new HandheldContainer(containerId, HandheldContainerKind.Pail, capacity);
            HandOperationResult pickedUp = authority.PickUpContainer(
                pail, requirement, hand, Context("test-pail-item", 2, causeId));
            Assert.IsTrue(pickedUp.Succeeded, pickedUp.Reason);
            return pail;
        }

        private static WornContainer WornSatchel(int personId, string containerId, WornContainerCapacity capacity)
        {
            var satchel = new WornContainer(containerId, WornContainerKind.Satchel, capacity);
            WornContainerOperationResult worn = satchel.Wear(personId);
            Assert.IsTrue(worn.Succeeded, worn.Reason);
            return satchel;
        }

        /// <summary>
        /// Registers the test-only item specs. Identical re-registration
        /// is idempotent, so repeated test runs are safe. Test-only
        /// numbers, not authored item measurements: the test item is
        /// 0.5 kg, 1.0 liter, 0.2 by 0.15 by 0.1 m per unit; the long test
        /// item is the same except 0.6 m long, so it exceeds the standard
        /// 0.5 m interior length.
        /// </summary>
        private static void RegisterTestSpecs()
        {
            Assert.IsNull(PhysicalItemSpecRegistry.Register(new PhysicalItemSpec(
                "test-pail-item", "Test Pail Item", "piece",
                PhysicalMeasure.Known(0.5f), PhysicalMeasure.Known(1.0f),
                PhysicalDimensions.Known(0.2f, 0.15f, 0.1f),
                containerCompatible: true, perishable: true, handlingNote: string.Empty)));
            Assert.IsNull(PhysicalItemSpecRegistry.Register(new PhysicalItemSpec(
                "test-pail-long-item", "Test Pail Long Item", "piece",
                PhysicalMeasure.Known(0.5f), PhysicalMeasure.Known(1.0f),
                PhysicalDimensions.Known(0.6f, 0.15f, 0.1f),
                containerCompatible: true, perishable: false, handlingNote: string.Empty)));
        }

        private static PerishableLotState.SaveDto FreshDto(LotKey lotKey, string itemId, int units) =>
            new PerishableLotState(lotKey, itemId, units, DayIndex)
                .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Fresh)
                .CaptureSaveDto();

        /// <summary>
        /// Minimal read-model driver: a real shopping loop over fresh
        /// registries (no trips run, so custody is empty) plus a
        /// dictionary of real hand authorities, a dictionary of real worn
        /// containers, and a dictionary of real held pails, each keyed by
        /// person id. The suppliers map a person id to that person's
        /// snapshots and return null for an unknown person.
        /// </summary>
        private sealed class Driver
        {
            public HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public Dictionary<int, HandOccupancyAuthority> Hands = new Dictionary<int, HandOccupancyAuthority>();
            public Dictionary<int, List<WornContainer>> Satchels = new Dictionary<int, List<WornContainer>>();
            public Dictionary<int, List<HandheldContainer>> Pails = new Dictionary<int, List<HandheldContainer>>();

            public HandOccupancyAuthority AuthorityFor(int personId)
            {
                if (!Hands.TryGetValue(personId, out HandOccupancyAuthority authority))
                {
                    authority = new HandOccupancyAuthority(personId);
                    Hands[personId] = authority;
                }

                return authority;
            }

            public void AddSatchel(int personId, WornContainer container)
            {
                if (!Satchels.TryGetValue(personId, out List<WornContainer> list))
                {
                    list = new List<WornContainer>();
                    Satchels[personId] = list;
                }

                list.Add(container);
            }

            public void AddPail(int personId, HandheldContainer container)
            {
                if (!Pails.TryGetValue(personId, out List<HandheldContainer> list))
                {
                    list = new List<HandheldContainer>();
                    Pails[personId] = list;
                }

                list.Add(container);
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
                    pid => Hands.TryGetValue(pid, out HandOccupancyAuthority authority)
                        ? authority.Current : null,
                    null,
                    pid =>
                    {
                        if (!Satchels.TryGetValue(pid, out List<WornContainer> list)) return null;
                        var snapshots = new List<WornContainerSnapshot>();
                        foreach (WornContainer container in list)
                        {
                            snapshots.Add(container.Current);
                        }

                        return snapshots.AsReadOnly();
                    },
                    pid =>
                    {
                        if (!Pails.TryGetValue(pid, out List<HandheldContainer> list)) return null;
                        var snapshots = new List<HandheldContainerSnapshot>();
                        foreach (HandheldContainer container in list)
                        {
                            snapshots.Add(container.Current);
                        }

                        return snapshots.AsReadOnly();
                    });
            }

            public PersonInventoryReadModel ReadModelWithoutHeldSupplier()
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
                    pid => Hands.TryGetValue(pid, out HandOccupancyAuthority authority)
                        ? authority.Current : null,
                    null,
                    pid =>
                    {
                        if (!Satchels.TryGetValue(pid, out List<WornContainer> list)) return null;
                        var snapshots = new List<WornContainerSnapshot>();
                        foreach (WornContainer container in list)
                        {
                            snapshots.Add(container.Current);
                        }

                        return snapshots.AsReadOnly();
                    });
            }
        }

        [Test]
        public void PickUpPail_OneHandedOccupiesNamedHand()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());
            Assert.AreEqual(2, authority.FreeHandCount);

            HandOperationResult result = authority.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-pickup-one-1"));

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(result.LotKey.Equals(PailOwnKey("pail-1")));
            Assert.AreEqual(string.Empty, result.BatchId);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
            Assert.AreEqual(string.Empty, authority.Current.LeftBatchId);
            Assert.IsTrue(authority.IsFree(HandSlot.Right));
            Assert.AreEqual(PlayerId, pail.HeldByPersonId);
            Assert.AreEqual(HandSlot.Left, pail.HeldHand);
            PersonInventoryReadModel.HandOccupancyView view =
                driver.ReadModel().HandOccupancy(PlayerId);
            Assert.IsTrue(!view.Left.IsFree);
            Assert.IsTrue(view.Left.LotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(view.Right.IsFree);
            Assert.AreEqual(1, view.FreeHandCount);
        }

        [Test]
        public void PickUpPail_TwoHandedOccupiesBothHands()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());

            HandOperationResult result = authority.PickUpContainer(
                pail, HandRequirement.TwoHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-pickup-two-1"));

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(authority.Current.RightLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(authority.Current.IsTwoHandedHold);
            Assert.AreEqual(0, authority.FreeHandCount);
            Assert.AreEqual(PlayerId, pail.HeldByPersonId);
        }

        [Test]
        public void PickUpPail_RequiresRequirement()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());

            HandOperationResult noRequirement = authority.PickUpContainer(pail, null, HandSlot.Left);
            HandOperationResult unknown = authority.PickUpContainer(
                pail, HandRequirement.Unknown, HandSlot.Left);
            HandOperationResult notCarryable = authority.PickUpContainer(
                pail, HandRequirement.NoHandCarryable, HandSlot.Left);

            Assert.IsTrue(!noRequirement.Succeeded);
            Assert.IsTrue(!unknown.Succeeded);
            Assert.IsTrue(!notCarryable.Succeeded);
            Assert.AreEqual(2, authority.FreeHandCount);
            Assert.AreEqual(-1, pail.HeldByPersonId);
            Assert.IsTrue(!pail.HeldHand.HasValue);
        }

        [Test]
        public void PickUpPail_IntoOccupiedHand_Refused()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-occupied-pickup-1"));

            HandOperationResult refused = authority.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-occupied-pickup-2"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("lot-test-item-1"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.AreEqual(-1, pail.HeldByPersonId);
        }

        [Test]
        public void PickUpPail_ByAnotherPerson_RefusedNamingHolder()
        {
            var first = new HandOccupancyAuthority(PlayerId);
            var second = new HandOccupancyAuthority(NpcId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());
            Assert.IsTrue(first.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-other-pickup-1")).Succeeded);

            HandOperationResult refused = second.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-other-pickup-2"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("101"), refused.Reason);
            Assert.AreEqual(PlayerId, pail.HeldByPersonId);
            Assert.AreEqual(HandSlot.Left, pail.HeldHand);
        }

        [Test]
        public void PickUpPail_SamePersonSameHand_SucceedsIdempotently()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());
            Assert.IsTrue(authority.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-idempotent-pickup-1")).Succeeded);

            HandOperationResult second = authority.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-idempotent-pickup-2"));

            Assert.IsTrue(second.Succeeded, second.Reason);
            Assert.AreEqual(PlayerId, pail.HeldByPersonId);
            Assert.AreEqual(HandSlot.Left, pail.HeldHand);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
        }

        [Test]
        public void PickUpPail_SamePersonOtherHand_RefusedNamingCurrentHand()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());
            Assert.IsTrue(authority.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-other-hand-pickup-1")).Succeeded);

            HandOperationResult refused = authority.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-other-hand-pickup-2"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("left"), refused.Reason);
            Assert.AreEqual(HandSlot.Left, pail.HeldHand);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(authority.IsFree(HandSlot.Right));
        }

        [Test]
        public void PailInOneHandPlusLotInOtherHand_Allowed()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());
            Assert.IsTrue(authority.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-both-hands-pickup-1")).Succeeded);

            HandOperationResult allocated = authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-both-hands-pickup-2"));

            Assert.IsTrue(allocated.Succeeded, allocated.Reason);
            Assert.AreEqual(0, authority.FreeHandCount);
            Assert.IsTrue(!authority.Current.IsTwoHandedHold);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
        }

        [Test]
        public void PlaceLotIntoHeldPail_OtherHandBecomesFreeAndPailHoldsLotOnce()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-held-place-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-held-place-pickup-2"));

            HandOperationResult result = authority.TransferToHeldContainer(
                HandSlot.Right, pail,
                Context("test-pail-item", 2, "p2b-held-place-1"),
                FreshDto(TestItemLot(), "test-pail-item", 2));

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(result.LotKey.Equals(TestItemLot()));
            Assert.AreEqual("carry-12-1", result.BatchId);
            Assert.IsTrue(authority.IsFree(HandSlot.Right));
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(pail.Contains(TestItemLot()));
            Assert.AreEqual(1, pail.Entries.Count);
            WornContainerEntry entry = pail.EntryFor(TestItemLot());
            Assert.IsNotNull(entry);
            Assert.IsTrue(entry.LotKey.Equals(TestItemLot()));
            Assert.AreEqual("carry-12-1", entry.BatchId);
            Assert.AreEqual("test-pail-item", entry.ItemId);
            Assert.AreEqual(2, entry.Units);
            Assert.AreEqual(PersonItemCondition.Fresh, entry.Condition);
        }

        [Test]
        public void PlaceLotIntoSetDownPail_LotHandBecomesFreeAndPailStaysSetDown()
        {
            RegisterTestSpecs();
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-setdown-place-pickup-1");
            driver.AddPail(PlayerId, pail);
            Assert.IsTrue(authority.SetDownContainer(HandSlot.Left, pail, "workshop:millbrook").Succeeded);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-setdown-place-pickup-2"));

            HandOperationResult result = authority.TransferToHeldContainer(
                HandSlot.Left, pail,
                Context("test-pail-item", 2, "p2b-setdown-place-1"),
                FreshDto(TestItemLot(), "test-pail-item", 2));

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
            Assert.AreEqual(-1, pail.HeldByPersonId);
            Assert.IsTrue(pail.Contains(TestItemLot()));
            PersonInventoryReadModel.HeldContainerContents contents =
                driver.ReadModel().ItemsInHeldContainers(PlayerId);
            Assert.AreEqual(0, contents.Containers.Count);
            Assert.AreEqual(0, contents.Items.Count);
        }

        [Test]
        public void RetrieveLot_HandHoldsItAgainAndPailEmpty()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-retrieve-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-retrieve-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-retrieve-place-1")).Succeeded);

            HandOperationResult retrieved = authority.TransferFromHeldContainer(
                pail, TestItemLot(), HandSlot.Right, HandRequirement.OneHanded,
                Context("test-pail-item", 2, "p2b-retrieve-1"));

            Assert.IsTrue(retrieved.Succeeded, retrieved.Reason);
            Assert.IsTrue(retrieved.LotKey.Equals(TestItemLot()));
            Assert.AreEqual("carry-12-1", retrieved.BatchId);
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, pail.TotalUnits);
            Assert.AreEqual(0, pail.Entries.Count);
            Assert.IsTrue(!pail.Contains(TestItemLot()));
        }

        [Test]
        public void RetrieveFromSetDownPail_HandHoldsItAgain()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-setdown-retrieve-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-setdown-retrieve-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-setdown-retrieve-place-1")).Succeeded);
            Assert.IsTrue(authority.SetDownContainer(HandSlot.Left, pail, "workshop:millbrook").Succeeded);

            HandOperationResult retrieved = authority.TransferFromHeldContainer(
                pail, TestItemLot(), HandSlot.Left, HandRequirement.OneHanded,
                Context("test-pail-item", 2, "p2b-setdown-retrieve-1"));

            Assert.IsTrue(retrieved.Succeeded, retrieved.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, pail.Entries.Count);
            Assert.AreEqual(-1, pail.HeldByPersonId);
        }

        [Test]
        public void RetrieveTwoHandedLot_FillsBothHands()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());
            authority.AllocateTwoHanded(
                TestItemLot(), "carry-12-1", HandRequirement.TwoHanded,
                Context("test-pail-item", 2, "p2b-twohanded-pickup-1"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Left, pail, Context("test-pail-item", 2, "p2b-twohanded-place-1")).Succeeded);
            Assert.AreEqual(2, authority.FreeHandCount);

            HandOperationResult retrieved = authority.TransferFromHeldContainer(
                pail, TestItemLot(), HandSlot.Left, HandRequirement.TwoHanded,
                Context("test-pail-item", 2, "p2b-twohanded-retrieve-1"));

            Assert.IsTrue(retrieved.Succeeded, retrieved.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
            Assert.IsTrue(authority.Current.IsTwoHandedHold);
            Assert.AreEqual(0, authority.FreeHandCount);
            Assert.AreEqual(0, pail.Entries.Count);
        }

        [Test]
        public void RetrieveIntoOccupiedHand_RefusedAndPailKeepsLot()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-occupied-retrieve-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-occupied-retrieve-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-occupied-retrieve-place-1")).Succeeded);
            authority.AllocateOneHanded(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Right,
                Context("flour", 2, "p2b-occupied-retrieve-pickup-3"));

            HandOperationResult refused = authority.TransferFromHeldContainer(
                pail, TestItemLot(), HandSlot.Right, HandRequirement.OneHanded,
                Context("test-pail-item", 2, "p2b-occupied-retrieve-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("occupied"), refused.Reason);
            Assert.IsTrue(authority.Current.RightLotKey.Equals(FlourLot()));
            Assert.IsTrue(pail.Contains(TestItemLot()));
            Assert.AreEqual(1, pail.Entries.Count);
        }

        [Test]
        public void RetrieveFromPail_NotHoldingLot_Refused()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-missing-retrieve-pickup-1");

            HandOperationResult refused = authority.TransferFromHeldContainer(
                pail, TestItemLot(), HandSlot.Right, HandRequirement.OneHanded,
                Context("test-pail-item", 2, "p2b-missing-retrieve-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.AreEqual(1, authority.FreeHandCount);
            Assert.IsTrue(authority.IsFree(HandSlot.Right));
            Assert.AreEqual(0, pail.Entries.Count);
        }

        [Test]
        public void RetrieveIntoContainerHand_Refused()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-container-hand-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-container-hand-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-container-hand-place-1")).Succeeded);

            HandOperationResult refused = authority.TransferFromHeldContainer(
                pail, TestItemLot(), HandSlot.Left, HandRequirement.OneHanded,
                Context("test-pail-item", 2, "p2b-container-hand-retrieve-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("occupied"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(pail.Contains(TestItemLot()));
            Assert.AreEqual(1, pail.Entries.Count);
        }

        [Test]
        public void SetDownPail_HandFreeContentsStayInsideAndRetainLocation()
        {
            RegisterTestSpecs();
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-setdown-pickup-1");
            driver.AddPail(PlayerId, pail);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-setdown-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-setdown-place-1")).Succeeded);

            HandOperationResult setDown = authority.SetDownContainer(HandSlot.Left, pail, "workshop:millbrook");

            Assert.IsTrue(setDown.Succeeded, setDown.Reason);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
            Assert.AreEqual(-1, pail.HeldByPersonId);
            Assert.IsTrue(!pail.HeldHand.HasValue);
            Assert.AreEqual("workshop:millbrook", pail.LocationId);
            Assert.AreEqual("workshop:millbrook", pail.Current.LocationId);
            Assert.IsTrue(pail.Contains(TestItemLot()));
            Assert.AreEqual(1, pail.Current.Entries.Count);
            PersonInventoryReadModel.HeldContainerContents contents =
                driver.ReadModel().ItemsInHeldContainers(PlayerId);
            Assert.AreEqual(0, contents.Containers.Count);
            Assert.AreEqual(0, contents.Items.Count);
        }

        [Test]
        public void SetDownPail_RequiresLocationAndRegistryKeepsItAccessible()
        {
            var placementLog = new CustodyTransferLog();
            var authority = new HandOccupancyAuthority(PlayerId, placementLog);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-located", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "located-pickup");
            var registry = new HandheldContainerRegistry();
            Assert.IsTrue(registry.Register(pail, out string registrationRefusal), registrationRefusal);

            HandOperationResult missingLocation = authority.SetDownContainer(HandSlot.Left, pail);
            Assert.IsFalse(missingLocation.Succeeded);
            Assert.AreEqual(PlayerId, pail.HeldByPersonId);
            Assert.AreEqual(HandSlot.Left, pail.HeldHand);
            Assert.AreEqual(0, registry.AtLocation("workshop:millbrook").Count);

            HandOperationResult setDown = authority.SetDownContainer(
                HandSlot.Left, pail, " workshop:millbrook ",
                Context("test-pail-item", 1, "placed-pail-setdown"));
            Assert.IsTrue(setDown.Succeeded, setDown.Reason);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
            Assert.AreEqual("workshop:millbrook", pail.LocationId);
            Assert.AreEqual(1, registry.AtLocation("workshop:millbrook").Count);
            Assert.AreEqual("pail-located", registry.AtLocation("workshop:millbrook")[0].ContainerId);
            Assert.AreEqual(0, registry.HeldByPerson(PlayerId).Count);
            CustodyTransferRecord setDownRecord = placementLog.LatestForCause(
                CustodyTransferLog.CauseContainerSetDown, "placed-pail-setdown");
            Assert.IsNotNull(setDownRecord);
            Assert.IsTrue(setDownRecord.SourceLotKey.Equals(pail.OwnKey));
            Assert.IsTrue(setDownRecord.DestinationLotKey.Equals(pail.OwnKey));
            Assert.AreEqual("location:workshop:millbrook", setDownRecord.ToCustodian);

            Assert.IsTrue(authority.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 1, "placed-pail-pickup")).Succeeded);
            Assert.AreEqual(string.Empty, pail.LocationId);
            Assert.AreEqual(0, registry.AtLocation("workshop:millbrook").Count);
            Assert.AreEqual(1, registry.HeldByPerson(PlayerId).Count);
            CustodyTransferRecord pickupRecord = placementLog.LatestForCause(
                CustodyTransferLog.CauseContainerPickup, "placed-pail-pickup");
            Assert.IsNotNull(pickupRecord);
            Assert.AreEqual("location:workshop:millbrook", pickupRecord.FromCustodian);
            Assert.AreEqual("person:101", pickupRecord.ToCustodian);
        }

        [Test]
        public void HandheldContainerRegistry_RejectsDuplicateIdentityButAllowsSameRegistration()
        {
            var registry = new HandheldContainerRegistry();
            var first = new HandheldContainer("pail-global-id", HandheldContainerKind.Pail);
            var second = new HandheldContainer("pail-global-id", HandheldContainerKind.Pail);

            Assert.IsTrue(registry.Register(first, out string firstRefusal), firstRefusal);
            Assert.IsTrue(registry.Register(first, out string repeatRefusal), repeatRefusal);
            Assert.IsFalse(registry.Register(second, out string duplicateRefusal));
            Assert.IsTrue(duplicateRefusal.Contains("already assigned"), duplicateRefusal);
            Assert.AreSame(first, registry.Find("pail-global-id"));
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void HandheldContainerRegistry_RestoreRejectsSameLotInTwoPailsAtomically()
        {
            LotKey duplicatedLot = TestItemLot();
            var first = new HandheldContainer("pail-a", HandheldContainerKind.Pail);
            var second = new HandheldContainer("pail-b", HandheldContainerKind.Pail);
            var entry = new WornContainerEntry(
                duplicatedLot, "batch-1", "test-pail-item", 1, PersonItemCondition.Fresh);
            Assert.IsTrue(first.RestoreEntries(new[] { entry }, out string firstRefusal), firstRefusal);
            Assert.IsTrue(second.RestoreEntries(new[] { entry }, out string secondRefusal), secondRefusal);
            Assert.IsTrue(first.RestorePlacement(-1, null, "storage:first", out firstRefusal), firstRefusal);
            Assert.IsTrue(second.RestorePlacement(-1, null, "storage:second", out secondRefusal), secondRefusal);

            var registry = new HandheldContainerRegistry();
            var existing = new HandheldContainer("pail-existing", HandheldContainerKind.Pail);
            Assert.IsTrue(registry.Register(existing, out string registerRefusal), registerRefusal);
            Assert.IsFalse(registry.RestoreSnapshots(
                new[] { first.Current, second.Current }, out string restoreRefusal));

            Assert.IsTrue(restoreRefusal.Contains("more than one pail"), restoreRefusal);
            Assert.AreEqual(1, registry.Count);
            Assert.AreSame(existing, registry.Find("pail-existing"));
            Assert.IsNull(registry.Find("pail-a"));
        }

        [Test]
        public void RegistryMediatedTransfer_RefusesLotAlreadyInAnotherPail()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer first = HeldPail(
                authority, PlayerId, "pail-first", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "registry-transfer-pickup-1");
            var second = new HandheldContainer("pail-second", HandheldContainerKind.Pail, StandardCapacity());
            Assert.IsTrue(second.RestorePlacement(-1, null, "workplace:bakery", out string placementRefusal), placementRefusal);
            var registry = new HandheldContainerRegistry();
            Assert.IsTrue(registry.Register(first, out string firstRefusal), firstRefusal);
            Assert.IsTrue(registry.Register(second, out string secondRefusal), secondRefusal);

            authority.AllocateOneHanded(
                TestItemLot(), "batch-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 1, "registry-transfer-source"));
            HandOperationResult firstTransfer = registry.TransferHandToContainer(
                authority, HandSlot.Right, first,
                Context("test-pail-item", 1, "registry-transfer-first"),
                FreshDto(TestItemLot(), "test-pail-item", 1));
            Assert.IsTrue(firstTransfer.Succeeded, firstTransfer.Reason);

            authority.AllocateOneHanded(
                TestItemLot(), "duplicate-batch", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 1, "registry-transfer-duplicate-source"));
            HandOperationResult duplicate = registry.TransferHandToContainer(
                authority, HandSlot.Right, second,
                Context("test-pail-item", 1, "registry-transfer-duplicate"));

            Assert.IsFalse(duplicate.Succeeded);
            Assert.IsTrue(duplicate.Reason.Contains("already inside pail 'pail-first'"), duplicate.Reason);
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
            Assert.IsTrue(first.Contains(TestItemLot()));
            Assert.IsFalse(second.Contains(TestItemLot()));
        }

        [Test]
        public void HandheldContainerSaveRestore_PreservesPlacementCapacityAndLotIdentityIdempotently()
        {
            RegisterTestSpecs();
            var sourceHands = new HandOccupancyAuthority(PlayerId);
            HandheldContainer sourcePail = HeldPail(
                sourceHands, PlayerId, "pail-save", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "pail-save-pickup-1");
            sourceHands.AllocateOneHanded(
                TestItemLot(), "batch-save", HandRequirement.OneHanded, HandSlot.Right);
            Assert.IsTrue(sourceHands.TransferToHeldContainer(
                HandSlot.Right, sourcePail, Context("test-pail-item", 2, "pail-save-place"),
                FreshDto(TestItemLot(), "test-pail-item", 2)).Succeeded);
            Assert.IsTrue(sourceHands.SetDownContainer(
                HandSlot.Left, sourcePail, "workplace:bakery").Succeeded);

            var sourceRegistry = new HandheldContainerRegistry();
            Assert.IsTrue(sourceRegistry.Register(sourcePail, out string registerRefusal), registerRefusal);
            var save = new Series2Pass9SaveState { HandheldContainers = sourceRegistry };
            var captured = save.Capture(new List<string>());

            var restoredRegistry = new HandheldContainerRegistry();
            var restore = new Series2Pass9SaveState { HandheldContainers = restoredRegistry };
            restore.Restore(captured, null, new List<string>());
            restore.Restore(captured, null, new List<string>());

            HandheldContainer restored = restoredRegistry.Find("pail-save");
            Assert.IsNotNull(restored);
            Assert.AreEqual("workplace:bakery", restored.LocationId);
            Assert.AreEqual(-1, restored.HeldByPersonId);
            Assert.AreEqual(1, restored.Entries.Count);
            Assert.IsTrue(restored.Entries[0].LotKey.Equals(TestItemLot()));
            Assert.AreEqual("batch-save", restored.Entries[0].BatchId);
            Assert.AreEqual(PersonItemCondition.Fresh, restored.Entries[0].Condition);
            Assert.AreEqual(1, restoredRegistry.AtLocation("workplace:bakery").Count);
            Assert.AreEqual(1, restoredRegistry.Count);
        }

        [Test]
        public void HandheldContainerSaveRestore_PreservesHeldContainerAndMatchingHand()
        {
            var sourceHands = new PersonHandOccupancyRegistry();
            sourceHands.EnsurePerson(PlayerId);
            var sourcePail = new HandheldContainer("pail-held-save", HandheldContainerKind.Pail);
            Assert.IsTrue(sourceHands.AllocateOneHanded(
                PlayerId, sourcePail.OwnKey, string.Empty, HandRequirement.OneHanded, HandSlot.Left).Succeeded);
            Assert.IsTrue(sourcePail.PickUp(PlayerId, HandSlot.Left).Succeeded);
            var sourceContainers = new HandheldContainerRegistry();
            Assert.IsTrue(sourceContainers.Register(sourcePail, out string registerRefusal), registerRefusal);

            var save = new Series2Pass9SaveState
            {
                PersonHands = sourceHands,
                HandheldContainers = sourceContainers,
            };
            Series2Pass9SaveDto captured = save.Capture(new List<string>());
            var restoredHands = new PersonHandOccupancyRegistry();
            var restoredContainers = new HandheldContainerRegistry();
            var restore = new Series2Pass9SaveState
            {
                PersonHands = restoredHands,
                HandheldContainers = restoredContainers,
            };
            restore.Restore(captured, null, new List<string>());

            HandOccupancy hand = restoredHands.SnapshotFor(PlayerId);
            HandheldContainer restoredPail = restoredContainers.Find("pail-held-save");
            Assert.IsNotNull(hand);
            Assert.IsNotNull(restoredPail);
            Assert.IsTrue(hand.LeftLotKey.Equals(restoredPail.OwnKey));
            Assert.AreEqual(PlayerId, restoredPail.HeldByPersonId);
            Assert.AreEqual(HandSlot.Left, restoredPail.HeldHand);
            Assert.AreEqual(string.Empty, restoredPail.LocationId);
        }

        [Test]
        public void ProductionShoppingLoop_ReadModelUsesHubHandheldContainerSupplier()
        {
            var inventories = new HouseholdInventoryRegistry();
            var membership = new HouseholdMembershipRegistry();
            var registry = new HandheldContainerRegistry();
            var hands = new PersonHandOccupancyRegistry();
            HandheldContainer pail = new HandheldContainer("pail-live-view", HandheldContainerKind.Pail);
            Assert.IsTrue(registry.Register(pail, out string registerRefusal), registerRefusal);
            hands.EnsurePerson(PlayerId);
            Assert.IsTrue(hands.AllocateOneHanded(
                PlayerId, pail.OwnKey, string.Empty, HandRequirement.OneHanded, HandSlot.Left).Succeeded);
            Assert.IsTrue(pail.PickUp(PlayerId, HandSlot.Left).Succeeded);

            var tracker = new PersonScheduleTracker();
            var loop = new HouseholdShoppingLoop(
                new PopulationState(), membership, new HouseholdLedgerRegistry(), inventories,
                new HouseholdNeedRegistry(), new SupplierDirectory(), new JourneyModel(),
                new TaskAuthority(), new WorkTimeBudgetStore(), tracker, new ShopperSellerKnowledge());
            loop.HandOccupancySupplier = hands.SnapshotFor;
            loop.HeldContainerSupplier = registry.HeldByPerson;

            PersonInventoryReadModel.HeldContainerContents view =
                loop.PersonInventory.ItemsInHeldContainers(PlayerId);

            Assert.AreEqual(1, view.Containers.Count);
            Assert.AreEqual("pail-live-view", view.Containers[0].ContainerId);
            Assert.AreEqual(0, view.Items.Count);
        }

        [Test]
        public void PickUpAgain_ContentsIntact()
        {
            RegisterTestSpecs();
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-again-pickup-1");
            driver.AddPail(PlayerId, pail);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-again-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-again-place-1")).Succeeded);
            Assert.IsTrue(authority.SetDownContainer(HandSlot.Left, pail, "workshop:millbrook").Succeeded);

            HandOperationResult pickedUp = authority.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-again-pickup-3"));

            Assert.IsTrue(pickedUp.Succeeded, pickedUp.Reason);
            Assert.AreEqual(1, pail.Entries.Count);
            WornContainerEntry entry = pail.EntryFor(TestItemLot());
            Assert.IsNotNull(entry);
            Assert.IsTrue(entry.LotKey.Equals(TestItemLot()));
            Assert.AreEqual("carry-12-1", entry.BatchId);
            PersonInventoryReadModel.HeldContainerContents contents =
                driver.ReadModel().ItemsInHeldContainers(PlayerId);
            Assert.AreEqual(1, contents.Containers.Count);
            Assert.AreEqual(1, contents.Items.Count);
            Assert.AreEqual("carry-12-1", contents.Items[0].BatchId);
            Assert.AreEqual("test-pail-item", contents.Items[0].ItemId);
        }

        [Test]
        public void MovePailBetweenHands_HandChangesAndContentsIntact()
        {
            RegisterTestSpecs();
            var log = new CustodyTransferLog();
            var authority = new HandOccupancyAuthority(PlayerId, log);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-move-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-move-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-move-place-1")).Succeeded);

            HandOperationResult moved = authority.MoveHeldContainer(
                pail, HandSlot.Left, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-move-1"));

            Assert.IsTrue(moved.Succeeded, moved.Reason);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
            Assert.IsTrue(authority.Current.RightLotKey.Equals(PailOwnKey("pail-1")));
            Assert.AreEqual(HandSlot.Right, pail.HeldHand);
            Assert.AreEqual(string.Empty, pail.LocationId);
            Assert.AreEqual(PlayerId, pail.HeldByPersonId);
            Assert.AreEqual(1, pail.Entries.Count);
            Assert.IsTrue(pail.Contains(TestItemLot()));
            int moveRecords = 0;
            foreach (CustodyTransferRecord record in log.Records)
            {
                if (record.CauseKind == CustodyTransferLog.CauseContainerMove) moveRecords++;
            }

            Assert.AreEqual(1, moveRecords);
            CustodyTransferRecord move = log.LatestForCause(CustodyTransferLog.CauseContainerMove, "p2b-move-1");
            Assert.IsNotNull(move);
            Assert.IsTrue(move.SourceLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(move.DestinationLotKey.Equals(PailOwnKey("pail-1")));
            Assert.AreEqual("person:101", move.FromCustodian);
            Assert.AreEqual("person:101", move.ToCustodian);
        }

        [Test]
        public void MovePailBetweenHands_ToOccupiedHand_Refused()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-move-occupied-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-move-occupied-pickup-2"));

            HandOperationResult refused = authority.MoveHeldContainer(pail, HandSlot.Left, HandSlot.Right);

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("occupied"), refused.Reason);
            Assert.AreEqual(HandSlot.Left, pail.HeldHand);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
        }

        [Test]
        public void MovePail_SameHand_Refused()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-move-same-pickup-1");

            HandOperationResult refused = authority.MoveHeldContainer(pail, HandSlot.Left, HandSlot.Left);

            Assert.IsTrue(!refused.Succeeded);
            Assert.AreEqual(HandSlot.Left, pail.HeldHand);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
        }

        [Test]
        public void MoveTwoHandedPail_RefusedNamingBothHands()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());
            Assert.IsTrue(authority.PickUpContainer(
                pail, HandRequirement.TwoHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-move-two-pickup-1")).Succeeded);

            HandOperationResult refused = authority.MoveHeldContainer(pail, HandSlot.Left, HandSlot.Right);

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("two-handed"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(authority.Current.RightLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(authority.Current.IsTwoHandedHold);
        }

        [Test]
        public void TwoHandedPail_BlocksSecondAction()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());
            Assert.IsTrue(authority.PickUpContainer(
                pail, HandRequirement.TwoHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-blocks-pickup-1")).Succeeded);

            HandOperationResult refused = authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-blocks-pickup-2"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("occupied"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(authority.Current.RightLotKey.Equals(PailOwnKey("pail-1")));
        }

        [Test]
        public void OverCapacityUnits_RefusedNamingUnitsAxis()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            // Test-only fixture: units cap 2, mass and volume ample.
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", KnownCapacity(2, 5f, 20f, 0.5f, 0.4f, 0.3f),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-units-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-units-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-units-place-1")).Succeeded);
            authority.AllocateOneHanded(
                TestItemLot2(), "carry-12-3", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 1, "p2b-units-pickup-3"));

            HandOperationResult refused = authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 1, "p2b-units-place-2"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("units"), refused.Reason);
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot2()));
            Assert.AreEqual(1, pail.Entries.Count);
            Assert.AreEqual(2, pail.TotalUnits);
        }

        [Test]
        public void OverCapacityMass_RefusedNamingMassAxis()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            // Test-only fixture: mass cap 1 kg; 3 test units weigh 1.5 kg.
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", KnownCapacity(10, 1f, 20f, 0.5f, 0.4f, 0.3f),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-mass-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 3, "p2b-mass-pickup-2"));

            HandOperationResult refused = authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 3, "p2b-mass-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("mass"), refused.Reason);
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, pail.Entries.Count);
        }

        [Test]
        public void OverCapacityVolume_RefusedNamingVolumeAxis()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            // Test-only fixture: volume cap 2 liters; 3 test units occupy 3 liters.
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", KnownCapacity(10, 5f, 2f, 0.5f, 0.4f, 0.3f),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-volume-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 3, "p2b-volume-pickup-2"));

            HandOperationResult refused = authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 3, "p2b-volume-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("volume"), refused.Reason);
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, pail.Entries.Count);
        }

        [Test]
        public void OversizeDimensions_RefusedNamingDimensionsAxis()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-dimensions-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-long-item", 1, "p2b-dimensions-pickup-2"));

            HandOperationResult refused = authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-long-item", 1, "p2b-dimensions-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("dimensions"), refused.Reason);
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, pail.Entries.Count);
        }

        [Test]
        public void UnknownCapacity_RefusesNamingMissingAxis()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail);
            Assert.IsTrue(authority.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-unknown-capacity-pickup-1")).Succeeded);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-unknown-capacity-pickup-2"));

            HandOperationResult refused = authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-unknown-capacity-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("units"), refused.Reason);
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, pail.Entries.Count);
        }

        [Test]
        public void UnknownItemMass_RefusesNamingBread()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-unknown-mass-pickup-1");
            authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("bread", 2, "p2b-unknown-mass-pickup-2"));

            HandOperationResult refused = authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("bread", 2, "p2b-unknown-mass-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("bread"), refused.Reason);
            Assert.IsTrue(refused.Reason.Contains("mass"), refused.Reason);
            Assert.IsTrue(authority.Current.RightLotKey.Equals(BreadLot()));
            Assert.AreEqual(0, pail.Entries.Count);
        }

        [Test]
        public void CircularNesting_PailIntoItself_Refused()
        {
            RegisterTestSpecs();
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());

            HandheldContainerOperationResult refused = pail.Place(
                PlayerId, PailOwnKey("pail-1"), "batch-self", "test-pail-item", 1, Evaluator(), DayIndex);

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("circular nesting"), refused.Reason);
            Assert.AreEqual(0, pail.Entries.Count);
        }

        [Test]
        public void DuplicatePlacement_SameLotTwice_Refused()
        {
            RegisterTestSpecs();
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());

            HandheldContainerOperationResult placed = pail.Place(
                PlayerId, TestItemLot(), "carry-12-1", "test-pail-item", 2, Evaluator(), DayIndex);
            HandheldContainerOperationResult refused = pail.Place(
                PlayerId, TestItemLot(), "carry-12-1", "test-pail-item", 2, Evaluator(), DayIndex);

            Assert.IsTrue(placed.Succeeded, placed.Reason);
            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("already holds"), refused.Reason);
            Assert.AreEqual(1, pail.Entries.Count);
        }

        [Test]
        public void ContainerAsContents_TransferPailIntoSatchel_Refused()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-as-contents-pickup-1");
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());

            HandOperationResult refused = authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-pail-item", 2, "p2b-as-contents-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("container-as-contents"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(PailOwnKey("pail-1")));
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void TransferIntoAnotherPersonsPail_Refused()
        {
            RegisterTestSpecs();
            var first = new HandOccupancyAuthority(PlayerId);
            var second = new HandOccupancyAuthority(NpcId);
            var pail = new HandheldContainer("pail-1", HandheldContainerKind.Pail, StandardCapacity());
            Assert.IsTrue(second.PickUpContainer(
                pail, HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-other-pail-pickup-1")).Succeeded);
            first.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-other-pail-pickup-2"));

            HandOperationResult refused = first.TransferToHeldContainer(
                HandSlot.Left, pail, Context("test-pail-item", 2, "p2b-other-pail-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("202"), refused.Reason);
            Assert.IsTrue(first.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, pail.Entries.Count);
        }

        [Test]
        public void RepeatedTransfer_PreservesTotals()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-repeated-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-repeated-pickup-2"));

            for (int round = 1; round <= 3; round++)
            {
                HandOperationResult placed = authority.TransferToHeldContainer(
                    HandSlot.Right, pail,
                    Context("test-pail-item", 2, "p2b-repeated-place-" + round));
                Assert.IsTrue(placed.Succeeded, placed.Reason);
                Assert.IsTrue(placed.LotKey.Equals(TestItemLot()));
                Assert.AreEqual("carry-12-1", placed.BatchId);
                Assert.IsTrue(authority.IsFree(HandSlot.Right));
                Assert.AreEqual(1, pail.Entries.Count);

                HandOperationResult retrieved = authority.TransferFromHeldContainer(
                    pail, TestItemLot(), HandSlot.Right, HandRequirement.OneHanded,
                    Context("test-pail-item", 2, "p2b-repeated-retrieve-" + round));
                Assert.IsTrue(retrieved.Succeeded, retrieved.Reason);
                Assert.IsTrue(retrieved.LotKey.Equals(TestItemLot()));
                Assert.AreEqual("carry-12-1", retrieved.BatchId);
                Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
                Assert.AreEqual(0, pail.Entries.Count);
            }
        }

        [Test]
        public void LogRecords_PickupPlaceRetrieveSetDown()
        {
            RegisterTestSpecs();
            var log = new CustodyTransferLog();
            var authority = new HandOccupancyAuthority(PlayerId, log);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-log-pickup-1");
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right);
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail,
                Context("test-pail-item", 2, "p2b-log-place-1"),
                FreshDto(TestItemLot(), "test-pail-item", 2)).Succeeded);
            Assert.IsTrue(authority.TransferFromHeldContainer(
                pail, TestItemLot(), HandSlot.Right, HandRequirement.OneHanded,
                Context("test-pail-item", 2, "p2b-log-retrieve-1")).Succeeded);
            Assert.IsTrue(authority.SetDownContainer(
                HandSlot.Left, pail, "workshop:millbrook",
                Context("test-pail-item", 2, "p2b-log-setdown-1")).Succeeded);

            Assert.AreEqual(4, log.Count);
            Assert.AreEqual(CustodyTransferLog.CauseContainerPickup, log.Records[0].CauseKind);
            Assert.AreEqual(CustodyTransferLog.CauseHandToContainer, log.Records[1].CauseKind);
            Assert.AreEqual(CustodyTransferLog.CauseContainerToHand, log.Records[2].CauseKind);
            Assert.AreEqual(CustodyTransferLog.CauseContainerSetDown, log.Records[3].CauseKind);
            CustodyTransferRecord pickup = log.LatestForCause(
                CustodyTransferLog.CauseContainerPickup, "p2b-log-pickup-1");
            Assert.IsNotNull(pickup);
            Assert.IsTrue(pickup.SourceLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(pickup.DestinationLotKey.Equals(PailOwnKey("pail-1")));
            Assert.AreEqual("container:pail-1", pickup.FromCustodian);
            Assert.AreEqual("person:101", pickup.ToCustodian);
            CustodyTransferRecord toContainer = log.LatestForCause(
                CustodyTransferLog.CauseHandToContainer, "p2b-log-place-1");
            Assert.IsNotNull(toContainer);
            Assert.IsTrue(toContainer.SourceLotKey.Equals(TestItemLot()));
            Assert.IsTrue(toContainer.DestinationLotKey.Equals(TestItemLot()));
            Assert.AreEqual("person:101", toContainer.FromCustodian);
            Assert.AreEqual("container:pail-1", toContainer.ToCustodian);
            CustodyTransferRecord toHand = log.LatestForCause(
                CustodyTransferLog.CauseContainerToHand, "p2b-log-retrieve-1");
            Assert.IsNotNull(toHand);
            Assert.AreEqual("container:pail-1", toHand.FromCustodian);
            Assert.AreEqual("person:101", toHand.ToCustodian);
            CustodyTransferRecord setDown = log.LatestForCause(
                CustodyTransferLog.CauseContainerSetDown, "p2b-log-setdown-1");
            Assert.IsNotNull(setDown);
            Assert.IsTrue(setDown.SourceLotKey.Equals(PailOwnKey("pail-1")));
            Assert.IsTrue(setDown.DestinationLotKey.Equals(PailOwnKey("pail-1")));
            Assert.AreEqual("person:101", setDown.FromCustodian);
            Assert.AreEqual("location:workshop:millbrook", setDown.ToCustodian);
        }

        [Test]
        public void ConditionPreserved_FreshStaysFresh()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-condition-pickup-1");
            PerishableLotState state = new PerishableLotState(TestItemLot(), "test-pail-item", 2, DayIndex)
                .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Fresh);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-condition-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail,
                Context("test-pail-item", 2, "p2b-condition-place-1"),
                FreshDto(TestItemLot(), "test-pail-item", 2)).Succeeded);

            WornContainerEntry entry = pail.EntryFor(TestItemLot());
            Assert.IsNotNull(entry);
            Assert.AreEqual(PersonItemCondition.Fresh, entry.Condition);
            PerishableLotState placed = state.TransferredTo("pail-1", DayIndex, "placed in pail");
            Assert.AreEqual("pail-1", placed.LocationOrContainer);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, placed.PhysicalCondition);

            Assert.IsTrue(authority.TransferFromHeldContainer(
                pail, TestItemLot(), HandSlot.Right, HandRequirement.OneHanded,
                Context("test-pail-item", 2, "p2b-condition-retrieve-1")).Succeeded);
            PerishableLotState retrieved = placed.TransferredTo("right hand", DayIndex, "retrieved to hand");
            Assert.AreEqual("right hand", retrieved.LocationOrContainer);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, retrieved.PhysicalCondition);
            Assert.AreEqual(0, pail.Entries.Count);
        }

        [Test]
        public void ReadModel_ListsHeldPailContents()
        {
            RegisterTestSpecs();
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-read-pickup-1");
            driver.AddPail(PlayerId, pail);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-read-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail,
                Context("test-pail-item", 2, "p2b-read-place-1"),
                FreshDto(TestItemLot(), "test-pail-item", 2)).Succeeded);

            PersonInventoryReadModel.HeldContainerContents contents =
                driver.ReadModel().ItemsInHeldContainers(PlayerId);

            Assert.AreEqual(string.Empty, contents.Reason);
            Assert.AreEqual(1, contents.Containers.Count);
            PersonInventoryReadModel.HeldContainerView view = contents.Containers[0];
            Assert.AreEqual("pail-1", view.ContainerId);
            Assert.AreEqual(HandheldContainerKind.Pail, view.Kind);
            Assert.AreEqual(PlayerId, view.HeldByPersonId);
            Assert.AreEqual(HandSlot.Left, view.HeldHand);
            Assert.AreEqual(1, view.Items.Count);
            PersonInventoryReadModel.ContainedItemView item = view.Items[0];
            Assert.IsTrue(item.LotKey.Equals(TestItemLot()));
            Assert.AreEqual("carry-12-1", item.BatchId);
            Assert.AreEqual("test-pail-item", item.ItemId);
            Assert.AreEqual(2, item.Units);
            Assert.AreEqual(PersonItemCondition.Fresh, item.Condition);
            Assert.AreEqual(1, contents.Items.Count);
            Assert.IsTrue(contents.Items[0].BatchId.Equals("carry-12-1"));
            Assert.AreEqual(PersonItemCondition.Fresh, contents.Items[0].Condition);
            Assert.AreEqual(PlayerId, contents.Items[0].HolderPersonId);
            Assert.AreEqual(-1, contents.Items[0].PurchaseDayIndex);
            Assert.AreEqual(-1, contents.Items[0].OwningHouseholdId);
            Assert.AreEqual(0, contents.Items[0].UnitPriceCents);
        }

        [Test]
        public void ReadModel_NoSupplier_ReturnsExistingEmptyReason()
        {
            RegisterTestSpecs();
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            driver.AddSatchel(PlayerId, satchel);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-pail-item", 2, "p2b-no-supplier-pickup-1"));
            Assert.IsTrue(authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-pail-item", 2, "p2b-no-supplier-place-1")).Succeeded);

            PersonInventoryReadModel readModel = driver.ReadModelWithoutHeldSupplier();
            PersonInventoryReadModel.HeldContainerContents held =
                readModel.ItemsInHeldContainers(PlayerId);

            Assert.AreEqual(0, held.Items.Count);
            Assert.AreEqual(0, held.Containers.Count);
            Assert.AreEqual(PersonInventoryReadModel.NoHeldContainersReason, held.Reason);
            PersonInventoryReadModel.CarriedContainerContents worn =
                readModel.ItemsInCarriedContainers(PlayerId);
            Assert.AreEqual(1, worn.Containers.Count);
            Assert.AreEqual("satchel-1", worn.Containers[0].ContainerId);
        }

        [Test]
        public void ReadModel_SetDownPail_NotListed()
        {
            RegisterTestSpecs();
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-setdown-read-pickup-1");
            driver.AddPail(PlayerId, pail);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-setdown-read-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-setdown-read-place-1")).Succeeded);
            Assert.IsTrue(authority.SetDownContainer(HandSlot.Left, pail, "workshop:millbrook").Succeeded);

            PersonInventoryReadModel.HeldContainerContents contents =
                driver.ReadModel().ItemsInHeldContainers(PlayerId);

            Assert.AreEqual(0, contents.Containers.Count);
            Assert.AreEqual(0, contents.Items.Count);
            Assert.AreEqual(1, pail.Current.Entries.Count);
            Assert.IsTrue(pail.Contains(TestItemLot()));
        }

        [Test]
        public void ReadModel_UnknownPerson_ReturnsEmptyWithoutError()
        {
            RegisterTestSpecs();
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-unknown-person-pickup-1");
            driver.AddPail(PlayerId, pail);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                Context("test-pail-item", 2, "p2b-unknown-person-pickup-2"));
            Assert.IsTrue(authority.TransferToHeldContainer(
                HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-unknown-person-place-1")).Succeeded);

            PersonInventoryReadModel.HeldContainerContents contents =
                driver.ReadModel().ItemsInHeldContainers(NpcId);

            Assert.AreEqual(0, contents.Containers.Count);
            Assert.AreEqual(0, contents.Items.Count);
            Assert.IsTrue(contents.Reason.Contains("202"), contents.Reason);
            Assert.IsTrue(contents.Reason.Contains("unknown to the held-container supplier"), contents.Reason);
        }

        [Test]
        public void ReadModel_HandView_NamesContainerHold()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandheldContainer pail = HeldPail(
                authority, PlayerId, "pail-1", StandardCapacity(),
                HandRequirement.OneHanded, HandSlot.Left, "p2b-hand-view-pickup-1");
            driver.AddPail(PlayerId, pail);

            PersonInventoryReadModel.HandOccupancyView view =
                driver.ReadModel().HandOccupancy(PlayerId);

            Assert.IsTrue(view.Left.LotKey.Equals(PailOwnKey("pail-1")));
            Assert.AreEqual(string.Empty, view.Left.ItemId);
            Assert.IsTrue(view.Reason.Contains("container"), view.Reason);
            Assert.IsTrue(view.Left.IsPartOfTwoHandedHold == false);
            Assert.IsTrue(view.Right.IsFree);
        }

        [Test]
        public void PlayerNpcParity_SamePickupPlaceRetrieve()
        {
            RegisterTestSpecs();
            foreach (int personId in new[] { PlayerId, NpcId })
            {
                var authority = new HandOccupancyAuthority(personId);
                HandheldContainer pail = HeldPail(
                    authority, personId, "pail-" + personId, StandardCapacity(),
                    HandRequirement.OneHanded, HandSlot.Left, "p2b-parity-pickup-" + personId);
                authority.AllocateOneHanded(
                    TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right,
                    Context("test-pail-item", 2, "p2b-parity-pickup-lot-" + personId));
                HandOperationResult placed = authority.TransferToHeldContainer(
                    HandSlot.Right, pail, Context("test-pail-item", 2, "p2b-parity-place-" + personId));
                Assert.IsTrue(placed.Succeeded, placed.Reason);
                Assert.IsTrue(authority.IsFree(HandSlot.Right));
                Assert.AreEqual(1, pail.Entries.Count);

                HandOperationResult retrieved = authority.TransferFromHeldContainer(
                    pail, TestItemLot(), HandSlot.Right, HandRequirement.OneHanded,
                    Context("test-pail-item", 2, "p2b-parity-retrieve-" + personId));
                Assert.IsTrue(retrieved.Succeeded, retrieved.Reason);
                Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
                Assert.AreEqual(0, pail.Entries.Count);

                HandOperationResult setDown = authority.SetDownContainer(HandSlot.Left, pail, "workshop:millbrook");
                Assert.IsTrue(setDown.Succeeded, setDown.Reason);
                Assert.AreEqual(-1, pail.HeldByPersonId);
            }
        }
    }
}
