// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Physical hands campaign P1b — hand occupancy read view and refusal
// diagnostics tests (plan section 6). Every test invokes the real
// HandOccupancyAuthority and the real PersonInventoryReadModel, not
// fixture copies. Player id 101 and NPC id 202 follow the V1 / U-3 parity
// convention: the same authority class with different person ids, no
// special-case branch.
//
// Covered: read view with no supplier, empty hands, one-handed and
// two-handed allocates, two different lots, release, unknown person,
// read-twice agreement, player/NPC parity; refusal diagnostics on refused
// allocate and release, clearing on a later success, and per-slot
// requirement retention; item id resolution for a deposited household lot,
// the carried-batch-key limit, and the missing-resolver gap; the hand
// cause move onto CustodyTransferLog.

using System.Collections.Generic;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class HandOccupancyReadViewTests
    {
        private const int PlayerId = 101;
        private const int NpcId = 202;

        private static LotKey BreadLot() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "lot-bread-1");

        private static LotKey FlourLot() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "lot-flour-1");

        private static LotKey ToolLot() =>
            new LotKey(CustodyTransferLog.BusinessAuthority("blacksmith-1"), "lot-hammer-1");

        private static LotKey CarriedBatchLot() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "carry-12-1");

        /// <summary>
        /// Minimal read-model driver: a real shopping loop over fresh
        /// registries (no trips run, so custody is empty) plus a
        /// dictionary of real hand authorities keyed by person id. The
        /// hand supplier maps a person id to that authority's Current
        /// snapshot and returns null for an unknown person.
        /// </summary>
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

            public PersonInventoryReadModel ReadModel(ILotIdentityResolver resolver = null)
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
                    resolver);
            }

            public PersonInventoryReadModel ReadModelWithoutSupplier()
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
                    new HouseholdShoppingReadModel(loop, tracker), Inventories);
            }
        }

        [Test]
        public void NoSupplier_ReturnsEmptyViewNamingMissingAuthority()
        {
            var driver = new Driver();
            PersonInventoryReadModel readModel = driver.ReadModelWithoutSupplier();

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.IsTrue(!view.HasHandAuthority);
            Assert.IsTrue(view.Left.IsFree);
            Assert.IsTrue(view.Right.IsFree);
            Assert.AreEqual(2, view.FreeHandCount);
            Assert.IsTrue(!view.IsTwoHandedHold);
            Assert.IsTrue(!view.HasRefusal);
            Assert.IsTrue(view.Reason.Contains("No hand authority was supplied"), view.Reason);
        }

        [Test]
        public void EmptyHands_ViewReportsBothHandsFree()
        {
            var driver = new Driver();
            driver.AuthorityFor(PlayerId);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.IsTrue(view.HasHandAuthority);
            Assert.AreEqual(PlayerId, view.PersonId);
            Assert.IsTrue(view.Left.IsFree);
            Assert.IsTrue(view.Right.IsFree);
            Assert.AreEqual(2, view.FreeHandCount);
            Assert.IsTrue(!view.IsTwoHandedHold);
            Assert.IsTrue(!view.HasRefusal);
            Assert.AreEqual(string.Empty, view.LastRefusalReason);
            Assert.AreEqual(string.Empty, view.Reason);
            Assert.AreEqual(HandRequirement.Unknown, view.Left.Requirement);
            Assert.AreEqual(HandRequirement.Unknown, view.Right.Requirement);
        }

        [Test]
        public void OneHandedAllocate_ViewReportsOccupyingLotAndBatchInExactlyThatHand()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandOperationResult held = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(held.Succeeded, held.Reason);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.IsTrue(!view.Left.IsFree);
            Assert.IsTrue(view.Left.LotKey.Equals(BreadLot()));
            Assert.AreEqual("carry-12-1", view.Left.BatchId);
            Assert.AreEqual(HandSlot.Left, view.Left.Hand);
            Assert.IsTrue(view.Right.IsFree);
            Assert.IsTrue(view.Right.LotKey.IsEmpty);
            Assert.AreEqual(1, view.FreeHandCount);
            Assert.IsTrue(!view.IsTwoHandedHold);
            Assert.IsTrue(!view.Left.IsPartOfTwoHandedHold);
            Assert.IsTrue(!view.Right.IsPartOfTwoHandedHold);
        }

        [Test]
        public void TwoHandedAllocate_ViewReportsSameLotInBothSlotsWithTwoHandedFlag()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandOperationResult held = authority.AllocateTwoHanded(
                ToolLot(), "carry-12-3", HandRequirement.TwoHanded);
            Assert.IsTrue(held.Succeeded, held.Reason);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.IsTrue(view.IsTwoHandedHold);
            Assert.AreEqual(0, view.FreeHandCount);
            Assert.IsTrue(view.Left.LotKey.Equals(ToolLot()));
            Assert.IsTrue(view.Right.LotKey.Equals(ToolLot()));
            Assert.AreEqual("carry-12-3", view.Left.BatchId);
            Assert.AreEqual("carry-12-3", view.Right.BatchId);
            Assert.IsTrue(view.Left.IsPartOfTwoHandedHold);
            Assert.IsTrue(view.Right.IsPartOfTwoHandedHold);
            Assert.AreEqual(HandRequirement.TwoHanded, view.Left.Requirement);
            Assert.AreEqual(HandRequirement.TwoHanded, view.Right.Requirement);
        }

        [Test]
        public void TwoDifferentLots_ViewReportsBothOccupiedAndNotTwoHanded()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            Assert.IsTrue(authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left).Succeeded);
            Assert.IsTrue(authority.AllocateOneHanded(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Right).Succeeded);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.IsTrue(!view.Left.IsFree);
            Assert.IsTrue(!view.Right.IsFree);
            Assert.AreEqual(0, view.FreeHandCount);
            Assert.IsTrue(view.Left.LotKey.Equals(BreadLot()));
            Assert.IsTrue(view.Right.LotKey.Equals(FlourLot()));
            Assert.IsTrue(!view.IsTwoHandedHold);
            Assert.IsTrue(!view.Left.IsPartOfTwoHandedHold);
            Assert.IsTrue(!view.Right.IsPartOfTwoHandedHold);
        }

        [Test]
        public void Release_ViewReportsHandFreeAgain()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            authority.AllocateOneHanded(FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Right);
            HandOperationResult released = authority.Release(HandSlot.Left);
            Assert.IsTrue(released.Succeeded, released.Reason);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.IsTrue(view.Left.IsFree);
            Assert.IsTrue(!view.Right.IsFree);
            Assert.IsTrue(view.Right.LotKey.Equals(FlourLot()));
            Assert.AreEqual(1, view.FreeHandCount);
            Assert.IsTrue(!view.IsTwoHandedHold);
        }

        [Test]
        public void UnknownPerson_ViewReturnsEmptyWithoutError()
        {
            var driver = new Driver();
            driver.AuthorityFor(PlayerId);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(999);

            Assert.IsTrue(view.HasHandAuthority);
            Assert.IsTrue(view.Left.IsFree);
            Assert.IsTrue(view.Right.IsFree);
            Assert.AreEqual(2, view.FreeHandCount);
            Assert.IsTrue(!view.IsTwoHandedHold);
            Assert.IsTrue(view.Reason.Contains("999"), view.Reason);
            Assert.IsTrue(view.Reason.Contains("unknown"), view.Reason);
        }

        [Test]
        public void ReadTwice_ViewAgrees()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView first = readModel.HandOccupancy(PlayerId);
            PersonInventoryReadModel.HandOccupancyView second = readModel.HandOccupancy(PlayerId);

            Assert.IsTrue(first.Left.LotKey.Equals(second.Left.LotKey));
            Assert.AreEqual(first.Left.BatchId, second.Left.BatchId);
            Assert.AreEqual(first.Left.Requirement, second.Left.Requirement);
            Assert.IsTrue(first.Right.LotKey.Equals(second.Right.LotKey));
            Assert.AreEqual(first.FreeHandCount, second.FreeHandCount);
            Assert.AreEqual(first.IsTwoHandedHold, second.IsTwoHandedHold);
            Assert.AreEqual(first.LastRefusalReason, second.LastRefusalReason);
            Assert.AreEqual(first.LastRefusalOperation, second.LastRefusalOperation);
            Assert.AreEqual(first.Reason, second.Reason);
        }

        [Test]
        public void PlayerNpcParity_ViewAgreesFor101And202()
        {
            var driver = new Driver();
            foreach (int personId in new[] { PlayerId, NpcId })
            {
                HandOccupancyAuthority authority = driver.AuthorityFor(personId);
                HandOperationResult held = authority.AllocateOneHanded(
                    BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
                Assert.IsTrue(held.Succeeded, held.Reason);
                HandOperationResult released = authority.Release(HandSlot.Left);
                Assert.IsTrue(released.Succeeded, released.Reason);
            }

            PersonInventoryReadModel readModel = driver.ReadModel();
            PersonInventoryReadModel.HandOccupancyView player = readModel.HandOccupancy(PlayerId);
            PersonInventoryReadModel.HandOccupancyView npc = readModel.HandOccupancy(NpcId);

            Assert.IsTrue(player.Left.IsFree);
            Assert.IsTrue(npc.Left.IsFree);
            Assert.AreEqual(player.FreeHandCount, npc.FreeHandCount);
            Assert.AreEqual(player.IsTwoHandedHold, npc.IsTwoHandedHold);
            Assert.AreEqual(player.HasRefusal, npc.HasRefusal);
            Assert.AreEqual(PlayerId, player.PersonId);
            Assert.AreEqual(NpcId, npc.PersonId);
        }

        [Test]
        public void RefusedAllocate_ViewReportsLastRefusalReasonAndOperation()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            HandOperationResult refused = authority.AllocateOneHanded(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(!refused.Succeeded);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.IsTrue(view.HasRefusal);
            Assert.AreEqual("AllocateOneHanded", view.LastRefusalOperation);
            Assert.IsTrue(view.LastRefusalReason.Contains("lot-bread-1"), view.LastRefusalReason);
            Assert.IsTrue(view.LastRefusalReason.Contains("left hand is occupied"), view.LastRefusalReason);
            Assert.IsTrue(view.Left.LotKey.Equals(BreadLot()));
        }

        [Test]
        public void SuccessfulAllocateAfterRefusal_ClearsLastRefusal()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            HandOperationResult refused = authority.AllocateOneHanded(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(!refused.Succeeded);
            PersonInventoryReadModel readModel = driver.ReadModel();
            Assert.IsTrue(readModel.HandOccupancy(PlayerId).HasRefusal);

            HandOperationResult released = authority.Release(HandSlot.Left);
            Assert.IsTrue(released.Succeeded, released.Reason);
            HandOperationResult held = authority.AllocateOneHanded(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(held.Succeeded, held.Reason);

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.IsTrue(!view.HasRefusal);
            Assert.AreEqual(string.Empty, view.LastRefusalReason);
            Assert.AreEqual(string.Empty, view.LastRefusalOperation);
            Assert.IsTrue(view.Left.LotKey.Equals(FlourLot()));
        }

        [Test]
        public void RefusedRelease_ViewReportsLastRefusal()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandOperationResult refused = authority.Release(HandSlot.Right);
            Assert.IsTrue(!refused.Succeeded);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.IsTrue(view.HasRefusal);
            Assert.AreEqual("Release", view.LastRefusalOperation);
            Assert.IsTrue(view.LastRefusalReason.Contains("right hand is already empty"), view.LastRefusalReason);
            Assert.AreEqual(2, view.FreeHandCount);
        }

        [Test]
        public void RequirementRetention_ViewReportsSuppliedRequirementPerSlot()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandOperationResult held = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(held.Succeeded, held.Reason);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView oneHanded = readModel.HandOccupancy(PlayerId);
            Assert.AreEqual(HandRequirement.OneHanded, oneHanded.Left.Requirement);
            Assert.AreEqual(HandRequirement.Unknown, oneHanded.Right.Requirement);

            var fresh = new HandOccupancyAuthority(NpcId);
            HandOperationResult twoHanded = fresh.AllocateTwoHanded(
                ToolLot(), "carry-12-3", HandRequirement.TwoHanded);
            Assert.IsTrue(twoHanded.Succeeded, twoHanded.Reason);
            driver.Hands[NpcId] = fresh;

            PersonInventoryReadModel.HandOccupancyView twoHandedView = readModel.HandOccupancy(NpcId);
            Assert.AreEqual(HandRequirement.TwoHanded, twoHandedView.Left.Requirement);
            Assert.AreEqual(HandRequirement.TwoHanded, twoHandedView.Right.Requirement);

            HandOperationResult released = fresh.Release(HandSlot.Left);
            Assert.IsTrue(released.Succeeded, released.Reason);
            PersonInventoryReadModel.HandOccupancyView afterRelease = readModel.HandOccupancy(NpcId);
            Assert.AreEqual(HandRequirement.Unknown, afterRelease.Left.Requirement);
            Assert.AreEqual(HandRequirement.Unknown, afterRelease.Right.Requirement);
            Assert.IsTrue(afterRelease.Left.IsFree);
            Assert.IsTrue(afterRelease.Right.IsFree);
        }

        [Test]
        public void ItemIdResolves_ForDepositedHouseholdLot()
        {
            var driver = new Driver();
            HouseholdInventory inventory = driver.Inventories.GetOrCreate(7);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 10, 0,
                "purchased 10u bread from General Store day 0 receipt r-1", ownerPersonId: PlayerId));
            var states = new List<HouseholdInventoryState>();
            driver.Inventories.ExportState(states);
            int lotId = -1;
            foreach (HouseholdInventoryState state in states)
            {
                if (state == null || state.Lots == null) continue;
                foreach (HouseholdItemLot lot in state.Lots)
                {
                    if (lot != null && lot.ItemId == HouseholdItemCatalog.BreadId) lotId = lot.LotId;
                }
            }

            Assert.IsTrue(lotId >= 0, "the deposited bread lot must have an assigned lot number");
            var lotKey = new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, lotId.ToString());
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandOperationResult held = authority.AllocateOneHanded(
                lotKey, "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(held.Succeeded, held.Reason);
            PersonInventoryReadModel readModel = driver.ReadModel(new LotIdentityResolver(driver.Inventories));

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.AreEqual(HouseholdItemCatalog.BreadId, view.Left.ItemId);
            Assert.AreEqual(string.Empty, view.Reason);
        }

        [Test]
        public void ItemIdEmpty_ForCarriedBatchKey()
        {
            var driver = new Driver();
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandOperationResult held = authority.AllocateOneHanded(
                CarriedBatchLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(held.Succeeded, held.Reason);
            PersonInventoryReadModel readModel = driver.ReadModel(new LotIdentityResolver(driver.Inventories));

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.AreEqual(string.Empty, view.Left.ItemId);
            Assert.IsTrue(view.Reason.Contains("carry-12-1"), view.Reason);
        }

        [Test]
        public void ItemIdEmpty_WhenNoResolverSupplied()
        {
            var driver = new Driver();
            HouseholdInventory inventory = driver.Inventories.GetOrCreate(7);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 10, 0,
                "purchased 10u bread from General Store day 0 receipt r-1", ownerPersonId: PlayerId));
            var states = new List<HouseholdInventoryState>();
            driver.Inventories.ExportState(states);
            int lotId = -1;
            foreach (HouseholdInventoryState state in states)
            {
                if (state == null || state.Lots == null) continue;
                foreach (HouseholdItemLot lot in state.Lots)
                {
                    if (lot != null && lot.ItemId == HouseholdItemCatalog.BreadId) lotId = lot.LotId;
                }
            }

            Assert.IsTrue(lotId >= 0, "the deposited bread lot must have an assigned lot number");
            var lotKey = new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, lotId.ToString());
            HandOccupancyAuthority authority = driver.AuthorityFor(PlayerId);
            HandOperationResult held = authority.AllocateOneHanded(
                lotKey, "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            Assert.IsTrue(held.Succeeded, held.Reason);
            PersonInventoryReadModel readModel = driver.ReadModel();

            PersonInventoryReadModel.HandOccupancyView view = readModel.HandOccupancy(PlayerId);

            Assert.AreEqual(string.Empty, view.Left.ItemId);
            Assert.IsTrue(view.Reason.Contains("resolver"), view.Reason);
        }

        [Test]
        public void CauseMove_ConstantsLiveOnCustodyTransferLog()
        {
            Assert.AreEqual("hand-pickup", CustodyTransferLog.CauseHandPickup);
            Assert.AreEqual("hand-release", CustodyTransferLog.CauseHandRelease);
            Assert.AreEqual("hand-to-container", CustodyTransferLog.CauseHandToContainer);
            Assert.AreEqual("container-to-hand", CustodyTransferLog.CauseContainerToHand);
        }

        [Test]
        public void CauseMove_LogRecordsUseLogConstants()
        {
            var log = new CustodyTransferLog();
            var authority = new HandOccupancyAuthority(PlayerId, log);
            var context = new HandCustodyContext(12, "bread", 14, "hand-test-pickup-1b");

            HandOperationResult held = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left, context);
            HandOperationResult released = authority.Release(
                HandSlot.Left, new HandCustodyContext(12, "bread", 14, "hand-test-release-1b"));

            Assert.IsTrue(held.Succeeded, held.Reason);
            Assert.IsTrue(released.Succeeded, released.Reason);
            Assert.AreEqual(2, log.Count);
            CustodyTransferRecord pickup = log.LatestForCause(CustodyTransferLog.CauseHandPickup, "hand-test-pickup-1b");
            Assert.IsNotNull(pickup);
            Assert.IsTrue(pickup.DestinationLotKey.Equals(BreadLot()));
            CustodyTransferRecord release = log.LatestForCause(CustodyTransferLog.CauseHandRelease, "hand-test-release-1b");
            Assert.IsNotNull(release);
            Assert.IsTrue(release.SourceLotKey.Equals(BreadLot()));
        }
    }
}
