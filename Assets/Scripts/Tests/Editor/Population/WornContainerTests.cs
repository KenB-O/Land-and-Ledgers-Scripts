// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Physical hands campaign P2a — one worn container kind, a satchel (plan
// section 8). Every test invokes the real WornContainer, the real
// HandOccupancyAuthority, the real PersonInventoryReadModel, the real
// PhysicalCapacityEvaluator, and the real CustodyTransferLog, not fixture
// copies. Player id 101 and NPC id 202 follow the V1 / U-3 parity
// convention: the same container class with different person ids, no
// special-case branch.
//
// TEST-ONLY FIXTURES: every capacity number and every test item spec
// number below is a test-only fixture, labelled as such, and is NOT an
// authored container measurement and NOT an authored item measurement.
// The test item ids ("test-satchel-item", "test-satchel-long-item") are
// not household catalog ids, so the catalog label check does not apply.
// The bread lot uses the seeded spec, which is Unknown on mass, volume,
// and dimensions — that Unknown is the point of the UnknownItemMass test.
//
// Covered: create and wear leaving both hands free; wear by another
// person refused naming the current wearer; wear twice idempotent;
// remove of an unworn satchel refused; hand-to-container place freeing
// the hand and holding the lot once; retrieval back into the hand;
// two-handed retrieval filling both hands; over-capacity refusals on
// the units, mass, volume, and dimensions axes; Unknown capacity
// refusing naming the missing axis; Unknown bread mass refusing naming
// bread; circular self-nesting refused; duplicate placement refused;
// transfer into another person's satchel refused; transfer into an
// unworn satchel refused; retrieval of a lot not held refused;
// retrieval into an occupied hand refused with the container keeping
// the lot; repeated transfer preserving totals; log records for both
// transfer directions; Fresh condition preserved through a move; the
// read model listing container contents, the no-supplier empty answer,
// the unworn-container invisibility, and the unknown-person empty
// answer; player and NPC parity.

using System.Collections.Generic;
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
    public sealed class WornContainerTests
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
        /// container measurement.
        /// </summary>
        private static WornContainerCapacity StandardCapacity() =>
            KnownCapacity(10, 5f, 20f, 0.5f, 0.4f, 0.3f);

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
                "test-satchel-item", "Test Satchel Item", "piece",
                PhysicalMeasure.Known(0.5f), PhysicalMeasure.Known(1.0f),
                PhysicalDimensions.Known(0.2f, 0.15f, 0.1f),
                containerCompatible: true, perishable: true, handlingNote: string.Empty)));
            Assert.IsNull(PhysicalItemSpecRegistry.Register(new PhysicalItemSpec(
                "test-satchel-long-item", "Test Satchel Long Item", "piece",
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
        /// dictionary of real hand authorities and a dictionary of real
        /// worn containers keyed by person id. Both suppliers map a
        /// person id to that person's snapshot and return null for an
        /// unknown person.
        /// </summary>
        private sealed class Driver
        {
            public HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public Dictionary<int, HandOccupancyAuthority> Hands = new Dictionary<int, HandOccupancyAuthority>();
            public Dictionary<int, List<WornContainer>> Containers = new Dictionary<int, List<WornContainer>>();

            public HandOccupancyAuthority AuthorityFor(int personId)
            {
                if (!Hands.TryGetValue(personId, out HandOccupancyAuthority authority))
                {
                    authority = new HandOccupancyAuthority(personId);
                    Hands[personId] = authority;
                }

                return authority;
            }

            public void AddContainer(int personId, WornContainer container)
            {
                if (!Containers.TryGetValue(personId, out List<WornContainer> list))
                {
                    list = new List<WornContainer>();
                    Containers[personId] = list;
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
                        if (!Containers.TryGetValue(pid, out List<WornContainer> list)) return null;
                        var snapshots = new List<WornContainerSnapshot>();
                        foreach (WornContainer container in list)
                        {
                            snapshots.Add(container.Current);
                        }

                        return snapshots.AsReadOnly();
                    });
            }

            public PersonInventoryReadModel ReadModelWithoutContainerSupplier()
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
                    null);
            }
        }

        [Test]
        public void CreateAndWear_SatchelLeavesBothHandsFree()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            var satchel = new WornContainer("satchel-1", WornContainerKind.Satchel, StandardCapacity());

            WornContainerOperationResult worn = satchel.Wear(PlayerId);

            Assert.IsTrue(worn.Succeeded, worn.Reason);
            Assert.AreEqual(PlayerId, satchel.WornByPersonId);
            Assert.AreEqual(WornContainerKind.Satchel, satchel.Kind);
            Assert.AreEqual(2, authority.FreeHandCount);
            PersonInventoryReadModel.HandOccupancyView view = driver.ReadModel().HandOccupancy(PlayerId);
            Assert.IsTrue(view.Left.IsFree);
            Assert.IsTrue(view.Right.IsFree);
            Assert.AreEqual(2, view.FreeHandCount);
        }

        [Test]
        public void WearByAnotherPerson_RefusedNamingCurrentWearer()
        {
            var satchel = new WornContainer("satchel-1", WornContainerKind.Satchel, StandardCapacity());
            Assert.IsTrue(satchel.Wear(PlayerId).Succeeded);

            WornContainerOperationResult refused = satchel.Wear(NpcId);

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("101"), refused.Reason);
            Assert.AreEqual(PlayerId, satchel.WornByPersonId);
        }

        [Test]
        public void WearSamePersonTwice_SucceedsIdempotently()
        {
            var satchel = new WornContainer("satchel-1", WornContainerKind.Satchel, StandardCapacity());

            Assert.IsTrue(satchel.Wear(PlayerId).Succeeded);
            WornContainerOperationResult second = satchel.Wear(PlayerId);

            Assert.IsTrue(second.Succeeded, second.Reason);
            Assert.AreEqual(PlayerId, satchel.WornByPersonId);
        }

        [Test]
        public void Remove_UnwornSatchel_Refused()
        {
            var satchel = new WornContainer("satchel-1", WornContainerKind.Satchel, StandardCapacity());

            WornContainerOperationResult refused = satchel.Remove();

            Assert.IsTrue(!refused.Succeeded);
            Assert.AreEqual(-1, satchel.WornByPersonId);
        }

        [Test]
        public void PlaceHandHeldLot_HandBecomesFreeAndContainerHoldsLotOnce()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-place-pickup-1"));

            HandOperationResult result = authority.TransferToContainer(
                HandSlot.Left, satchel,
                Context("test-satchel-item", 2, "p2a-place-1"),
                FreshDto(TestItemLot(), "test-satchel-item", 2));

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(result.LotKey.Equals(TestItemLot()));
            Assert.AreEqual("carry-12-1", result.BatchId);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
            Assert.IsTrue(satchel.Contains(TestItemLot()));
            Assert.AreEqual(1, satchel.Entries.Count);
            WornContainerEntry entry = satchel.EntryFor(TestItemLot());
            Assert.IsNotNull(entry);
            Assert.IsTrue(entry.LotKey.Equals(TestItemLot()));
            Assert.AreEqual("carry-12-1", entry.BatchId);
            Assert.AreEqual("test-satchel-item", entry.ItemId);
            Assert.AreEqual(2, entry.Units);
            Assert.AreEqual(PersonItemCondition.Fresh, entry.Condition);
        }

        [Test]
        public void RetrieveLot_HandHoldsItAgainAndContainerEmpty()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-retrieve-pickup-1"));
            Assert.IsTrue(authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-retrieve-place-1")).Succeeded);

            HandOperationResult retrieved = authority.TransferFromContainer(
                satchel, TestItemLot(), HandSlot.Left, HandRequirement.OneHanded,
                Context("test-satchel-item", 2, "p2a-retrieve-1"));

            Assert.IsTrue(retrieved.Succeeded, retrieved.Reason);
            Assert.IsTrue(retrieved.LotKey.Equals(TestItemLot()));
            Assert.AreEqual("carry-12-1", retrieved.BatchId);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, satchel.TotalUnits);
            Assert.AreEqual(0, satchel.Entries.Count);
            Assert.IsTrue(!satchel.Contains(TestItemLot()));
        }

        [Test]
        public void RetrieveTwoHandedLot_FillsBothHands()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            authority.AllocateTwoHanded(
                TestItemLot(), "carry-12-1", HandRequirement.TwoHanded,
                Context("test-satchel-item", 2, "p2a-twohanded-pickup-1"));
            Assert.IsTrue(authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-twohanded-place-1")).Succeeded);
            Assert.AreEqual(2, authority.FreeHandCount);

            HandOperationResult retrieved = authority.TransferFromContainer(
                satchel, TestItemLot(), HandSlot.Left, HandRequirement.TwoHanded,
                Context("test-satchel-item", 2, "p2a-twohanded-retrieve-1"));

            Assert.IsTrue(retrieved.Succeeded, retrieved.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.IsTrue(authority.Current.RightLotKey.Equals(TestItemLot()));
            Assert.IsTrue(authority.Current.IsTwoHandedHold);
            Assert.AreEqual(0, authority.FreeHandCount);
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void OverCapacityUnits_RefusedNamingUnitsAxis()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            // Test-only fixture: units cap 2, mass and volume ample.
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", KnownCapacity(2, 5f, 20f, 0.5f, 0.4f, 0.3f));
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-units-pickup-1"));
            Assert.IsTrue(authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-units-place-1")).Succeeded);
            authority.AllocateOneHanded(
                TestItemLot2(), "carry-12-3", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 1, "p2a-units-pickup-2"));

            HandOperationResult refused = authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 1, "p2a-units-place-2"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("units"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot2()));
            Assert.AreEqual(1, satchel.Entries.Count);
            Assert.AreEqual(2, satchel.TotalUnits);
        }

        [Test]
        public void OverCapacityMass_RefusedNamingMassAxis()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            // Test-only fixture: mass cap 1 kg; 3 test units weigh 1.5 kg.
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", KnownCapacity(10, 1f, 20f, 0.5f, 0.4f, 0.3f));
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 3, "p2a-mass-pickup-1"));

            HandOperationResult refused = authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 3, "p2a-mass-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("mass"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void OverCapacityVolume_RefusedNamingVolumeAxis()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            // Test-only fixture: volume cap 2 liters; 3 test units occupy 3 liters.
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", KnownCapacity(10, 5f, 2f, 0.5f, 0.4f, 0.3f));
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 3, "p2a-volume-pickup-1"));

            HandOperationResult refused = authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 3, "p2a-volume-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("volume"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void OversizeDimensions_RefusedNamingDimensionsAxis()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-long-item", 1, "p2a-dimensions-pickup-1"));

            HandOperationResult refused = authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-long-item", 1, "p2a-dimensions-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("dimensions"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void UnknownCapacity_RefusesNamingMissingAxis()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            var satchel = new WornContainer("satchel-1", WornContainerKind.Satchel);
            Assert.IsTrue(satchel.Wear(PlayerId).Succeeded);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-unknown-capacity-pickup-1"));

            HandOperationResult refused = authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-unknown-capacity-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("units"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void UnknownItemMass_RefusesNamingBread()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("bread", 2, "p2a-unknown-mass-pickup-1"));

            HandOperationResult refused = authority.TransferToContainer(
                HandSlot.Left, satchel, Context("bread", 2, "p2a-unknown-mass-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("bread"), refused.Reason);
            Assert.IsTrue(refused.Reason.Contains("mass"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(BreadLot()));
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void CircularNesting_ContainerIntoItself_Refused()
        {
            RegisterTestSpecs();
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            LotKey ownKey = new LotKey(CustodyTransferLog.ContainerAuthority, "satchel-1");

            WornContainerOperationResult refused = satchel.Place(
                PlayerId, ownKey, "batch-self", "test-satchel-item", 1, Evaluator(), DayIndex);

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("circular nesting"), refused.Reason);
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void DuplicatePlacement_SameLotTwice_Refused()
        {
            RegisterTestSpecs();
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());

            WornContainerOperationResult placed = satchel.Place(
                PlayerId, TestItemLot(), "carry-12-1", "test-satchel-item", 2, Evaluator(), DayIndex);
            WornContainerOperationResult refused = satchel.Place(
                PlayerId, TestItemLot(), "carry-12-1", "test-satchel-item", 2, Evaluator(), DayIndex);

            Assert.IsTrue(placed.Succeeded, placed.Reason);
            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("already holds"), refused.Reason);
            Assert.AreEqual(1, satchel.Entries.Count);
        }

        [Test]
        public void TransferIntoOtherPersonsSatchel_Refused()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            WornContainer satchel = WornSatchel(NpcId, "satchel-1", StandardCapacity());
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-other-person-pickup-1"));

            HandOperationResult refused = authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-other-person-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("202"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void TransferIntoUnwornSatchel_Refused()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            var satchel = new WornContainer("satchel-1", WornContainerKind.Satchel, StandardCapacity());
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-unworn-pickup-1"));

            HandOperationResult refused = authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-unworn-place-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("unworn"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void RetrieveFromContainer_NotHoldingLot_Refused()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());

            HandOperationResult refused = authority.TransferFromContainer(
                satchel, TestItemLot(), HandSlot.Left, HandRequirement.OneHanded,
                Context("test-satchel-item", 2, "p2a-retrieve-missing-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.AreEqual(2, authority.FreeHandCount);
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void RetrieveIntoOccupiedHand_RefusedAndContainerKeepsLot()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-occupied-pickup-1"));
            Assert.IsTrue(authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-occupied-place-1")).Succeeded);
            authority.AllocateOneHanded(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Left,
                Context("flour", 2, "p2a-occupied-pickup-2"));

            HandOperationResult refused = authority.TransferFromContainer(
                satchel, TestItemLot(), HandSlot.Left, HandRequirement.OneHanded,
                Context("test-satchel-item", 2, "p2a-occupied-retrieve-1"));

            Assert.IsTrue(!refused.Succeeded);
            Assert.IsTrue(refused.Reason.Contains("occupied"), refused.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(FlourLot()));
            Assert.IsTrue(satchel.Contains(TestItemLot()));
            Assert.AreEqual(1, satchel.Entries.Count);
        }

        [Test]
        public void RepeatedTransfer_PreservesTotals()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-repeated-pickup-1"));

            for (int round = 1; round <= 3; round++)
            {
                HandOperationResult placed = authority.TransferToContainer(
                    HandSlot.Left, satchel,
                    Context("test-satchel-item", 2, "p2a-repeated-place-" + round));
                Assert.IsTrue(placed.Succeeded, placed.Reason);
                Assert.IsTrue(placed.LotKey.Equals(TestItemLot()));
                Assert.AreEqual("carry-12-1", placed.BatchId);
                Assert.IsTrue(authority.IsFree(HandSlot.Left));
                Assert.AreEqual(1, satchel.Entries.Count);

                HandOperationResult retrieved = authority.TransferFromContainer(
                    satchel, TestItemLot(), HandSlot.Left, HandRequirement.OneHanded,
                    Context("test-satchel-item", 2, "p2a-repeated-retrieve-" + round));
                Assert.IsTrue(retrieved.Succeeded, retrieved.Reason);
                Assert.IsTrue(retrieved.LotKey.Equals(TestItemLot()));
                Assert.AreEqual("carry-12-1", retrieved.BatchId);
                Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
                Assert.AreEqual(0, satchel.Entries.Count);
            }
        }

        [Test]
        public void LogRecords_HandToContainerAndContainerToHand()
        {
            RegisterTestSpecs();
            var log = new CustodyTransferLog();
            var authority = new HandOccupancyAuthority(PlayerId, log);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-log-to-container-1")).Succeeded);
            Assert.IsTrue(authority.TransferFromContainer(
                satchel, TestItemLot(), HandSlot.Left, HandRequirement.OneHanded,
                Context("test-satchel-item", 2, "p2a-log-to-hand-1")).Succeeded);

            Assert.AreEqual(2, log.Count);
            CustodyTransferRecord toContainer = log.LatestForCause(
                CustodyTransferLog.CauseHandToContainer, "p2a-log-to-container-1");
            Assert.IsNotNull(toContainer);
            Assert.IsTrue(toContainer.SourceLotKey.Equals(TestItemLot()));
            Assert.IsTrue(toContainer.DestinationLotKey.Equals(TestItemLot()));
            Assert.AreEqual("person:101", toContainer.FromCustodian);
            Assert.AreEqual("container:satchel-1", toContainer.ToCustodian);
            Assert.AreEqual("test-satchel-item", toContainer.ItemId);
            Assert.AreEqual(2, toContainer.Units);
            CustodyTransferRecord toHand = log.LatestForCause(
                CustodyTransferLog.CauseContainerToHand, "p2a-log-to-hand-1");
            Assert.IsNotNull(toHand);
            Assert.IsTrue(toHand.SourceLotKey.Equals(TestItemLot()));
            Assert.IsTrue(toHand.DestinationLotKey.Equals(TestItemLot()));
            Assert.AreEqual("container:satchel-1", toHand.FromCustodian);
            Assert.AreEqual("person:101", toHand.ToCustodian);
        }

        [Test]
        public void ConditionPreserved_FreshStaysFresh()
        {
            RegisterTestSpecs();
            var authority = new HandOccupancyAuthority(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            PerishableLotState state = new PerishableLotState(TestItemLot(), "test-satchel-item", 2, DayIndex)
                .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Fresh);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-condition-pickup-1"));
            Assert.IsTrue(authority.TransferToContainer(
                HandSlot.Left, satchel,
                Context("test-satchel-item", 2, "p2a-condition-place-1"),
                FreshDto(TestItemLot(), "test-satchel-item", 2)).Succeeded);

            WornContainerEntry entry = satchel.EntryFor(TestItemLot());
            Assert.IsNotNull(entry);
            Assert.AreEqual(PersonItemCondition.Fresh, entry.Condition);
            PerishableLotState placed = state.TransferredTo("satchel-1", DayIndex, "placed in satchel");
            Assert.AreEqual("satchel-1", placed.LocationOrContainer);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, placed.PhysicalCondition);

            Assert.IsTrue(authority.TransferFromContainer(
                satchel, TestItemLot(), HandSlot.Left, HandRequirement.OneHanded,
                Context("test-satchel-item", 2, "p2a-condition-retrieve-1")).Succeeded);
            PerishableLotState retrieved = placed.TransferredTo("left hand", DayIndex, "retrieved to hand");
            Assert.AreEqual("left hand", retrieved.LocationOrContainer);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, retrieved.PhysicalCondition);
            Assert.AreEqual(0, satchel.Entries.Count);
        }

        [Test]
        public void ReadModel_ListsContainerContents()
        {
            RegisterTestSpecs();
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            driver.AddContainer(PlayerId, satchel);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-read-pickup-1"));
            Assert.IsTrue(authority.TransferToContainer(
                HandSlot.Left, satchel,
                Context("test-satchel-item", 2, "p2a-read-place-1"),
                FreshDto(TestItemLot(), "test-satchel-item", 2)).Succeeded);

            PersonInventoryReadModel.CarriedContainerContents contents =
                driver.ReadModel().ItemsInCarriedContainers(PlayerId);

            Assert.AreEqual(string.Empty, contents.Reason);
            Assert.AreEqual(1, contents.Containers.Count);
            PersonInventoryReadModel.CarriedContainerView view = contents.Containers[0];
            Assert.AreEqual("satchel-1", view.ContainerId);
            Assert.AreEqual(WornContainerKind.Satchel, view.Kind);
            Assert.AreEqual(PlayerId, view.WornByPersonId);
            Assert.AreEqual(1, view.Items.Count);
            PersonInventoryReadModel.ContainedItemView item = view.Items[0];
            Assert.IsTrue(item.LotKey.Equals(TestItemLot()));
            Assert.AreEqual("carry-12-1", item.BatchId);
            Assert.AreEqual("test-satchel-item", item.ItemId);
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
            var driver = new Driver();

            PersonInventoryReadModel.CarriedContainerContents contents =
                driver.ReadModelWithoutContainerSupplier().ItemsInCarriedContainers(PlayerId);

            Assert.AreEqual(0, contents.Items.Count);
            Assert.AreEqual(0, contents.Containers.Count);
            Assert.AreEqual(PersonInventoryReadModel.NoCarriedContainersReason, contents.Reason);
        }

        [Test]
        public void ReadModel_UnwornContainer_NotListed()
        {
            RegisterTestSpecs();
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            driver.AddContainer(PlayerId, satchel);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-unworn-read-pickup-1"));
            Assert.IsTrue(authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-unworn-read-place-1")).Succeeded);
            Assert.IsTrue(satchel.Remove().Succeeded);

            PersonInventoryReadModel.CarriedContainerContents contents =
                driver.ReadModel().ItemsInCarriedContainers(PlayerId);

            Assert.AreEqual(0, contents.Containers.Count);
            Assert.AreEqual(0, contents.Items.Count);
            Assert.AreEqual(1, satchel.Current.Entries.Count);
            Assert.IsTrue(satchel.Contains(TestItemLot()));
        }

        [Test]
        public void ReadModel_UnknownPerson_ReturnsEmptyWithoutError()
        {
            RegisterTestSpecs();
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            WornContainer satchel = WornSatchel(PlayerId, "satchel-1", StandardCapacity());
            driver.AddContainer(PlayerId, satchel);
            authority.AllocateOneHanded(
                TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("test-satchel-item", 2, "p2a-unknown-person-pickup-1"));
            Assert.IsTrue(authority.TransferToContainer(
                HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-unknown-person-place-1")).Succeeded);

            PersonInventoryReadModel.CarriedContainerContents contents =
                driver.ReadModel().ItemsInCarriedContainers(NpcId);

            Assert.AreEqual(0, contents.Containers.Count);
            Assert.AreEqual(0, contents.Items.Count);
            Assert.IsTrue(contents.Reason.Contains("202"), contents.Reason);
            Assert.IsTrue(contents.Reason.Contains("unknown to the container supplier"), contents.Reason);
        }

        [Test]
        public void PlayerNpcParity_SamePlaceAndRetrieve()
        {
            RegisterTestSpecs();
            foreach (int personId in new[] { PlayerId, NpcId })
            {
                var authority = new HandOccupancyAuthority(personId);
                WornContainer satchel = WornSatchel(personId, "satchel-" + personId, StandardCapacity());
                authority.AllocateOneHanded(
                    TestItemLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                    Context("test-satchel-item", 2, "p2a-parity-pickup-" + personId));
                HandOperationResult placed = authority.TransferToContainer(
                    HandSlot.Left, satchel, Context("test-satchel-item", 2, "p2a-parity-place-" + personId));
                Assert.IsTrue(placed.Succeeded, placed.Reason);
                Assert.IsTrue(authority.IsFree(HandSlot.Left));
                Assert.AreEqual(1, satchel.Entries.Count);

                HandOperationResult retrieved = authority.TransferFromContainer(
                    satchel, TestItemLot(), HandSlot.Left, HandRequirement.OneHanded,
                    Context("test-satchel-item", 2, "p2a-parity-retrieve-" + personId));
                Assert.IsTrue(retrieved.Succeeded, retrieved.Reason);
                Assert.IsTrue(authority.Current.LeftLotKey.Equals(TestItemLot()));
                Assert.AreEqual(0, satchel.Entries.Count);
            }
        }
    }
}
