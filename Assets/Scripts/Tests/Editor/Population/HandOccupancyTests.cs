// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Physical hands campaign P1a — hand authority core tests (plan section 8b).
// Every test invokes the real HandOccupancyAuthority, not a fixture copy.
// Player id 101 and NPC id 202 follow the V1 / U-3 parity convention: the
// same authority class with different person ids, no special-case branch.
//
// Covered: one-handed allocate to a free hand; allocate to an occupied hand
// refused; two-handed allocate with both hands free; two-handed allocate
// with one hand occupied refused; release of one hand frees exactly that
// hand; release of a two-handed hold frees both hands; release of an empty
// hand refused; allocate after release succeeds; two different lots in two
// hands allowed; the same lot in both hands through two one-handed
// allocates refused; player and NPC parity; lot identity preserved through
// allocate and release by LotKey; missing hand requirement refused;
// mismatched requirement refused; hand-to-container transfer into an
// unworn satchel refused with the unworn gap named (P2a rewrite of the
// P1a missing-container-type refusal, which no longer exists); log
// recording only when a log and a custody context are both supplied.

using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class HandOccupancyTests
    {
        private const int PlayerId = 101;
        private const int NpcId = 202;

        private static LotKey BreadLot() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "lot-bread-1");

        private static LotKey FlourLot() =>
            new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "lot-flour-1");

        private static LotKey ToolLot() =>
            new LotKey(CustodyTransferLog.BusinessAuthority("blacksmith-1"), "lot-hammer-1");

        private static HandCustodyContext Context(string causeId) =>
            new HandCustodyContext(12, "bread", 14, causeId);

        [Test]
        public void AllocateOneHanded_ToFreeHand_SucceedsAndOccupiesExactlyThatHand()
        {
            var authority = new HandOccupancyAuthority(PlayerId);

            HandOperationResult result = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(!authority.IsFree(HandSlot.Left));
            Assert.IsTrue(authority.IsFree(HandSlot.Right));
            Assert.AreEqual(1, authority.FreeHandCount);
            HandOccupancy current = authority.Current;
            Assert.IsTrue(current.LeftLotKey.Equals(BreadLot()));
            Assert.AreEqual("carry-12-1", current.LeftBatchId);
            Assert.IsTrue(current.RightLotKey.IsEmpty);
            Assert.IsTrue(!current.IsTwoHandedHold);
        }

        [Test]
        public void AllocateOneHanded_ToOccupiedHand_RefusedNamingOccupyingLot()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);

            HandOperationResult result = authority.AllocateOneHanded(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Left);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Reason.Contains("left hand is occupied"), result.Reason);
            Assert.IsTrue(result.Reason.Contains("lot-bread-1"), result.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(BreadLot()));
            Assert.IsTrue(authority.IsFree(HandSlot.Right));
        }

        [Test]
        public void AllocateTwoHanded_BothFree_SucceedsAndFillsBothSlotsWithSameLot()
        {
            var authority = new HandOccupancyAuthority(PlayerId);

            HandOperationResult result = authority.AllocateTwoHanded(
                ToolLot(), "carry-12-3", HandRequirement.TwoHanded);

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.AreEqual(0, authority.FreeHandCount);
            HandOccupancy current = authority.Current;
            Assert.IsTrue(current.LeftLotKey.Equals(ToolLot()));
            Assert.IsTrue(current.RightLotKey.Equals(ToolLot()));
            Assert.AreEqual("carry-12-3", current.LeftBatchId);
            Assert.AreEqual("carry-12-3", current.RightBatchId);
            Assert.IsTrue(current.IsTwoHandedHold);
        }

        [Test]
        public void AllocateTwoHanded_OneHandOccupied_RefusedNamingOccupyingLot()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right);

            HandOperationResult result = authority.AllocateTwoHanded(
                ToolLot(), "carry-12-3", HandRequirement.TwoHanded);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Reason.Contains("right hand is occupied"), result.Reason);
            Assert.IsTrue(result.Reason.Contains("lot-bread-1"), result.Reason);
            Assert.IsTrue(result.Reason.Contains("both hands free"), result.Reason);
            Assert.IsTrue(authority.Current.RightLotKey.Equals(BreadLot()));
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
        }

        [Test]
        public void ReleaseOneHand_FreesExactlyThatHand()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            authority.AllocateOneHanded(FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Right);

            HandOperationResult result = authority.Release(HandSlot.Left);

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(result.LotKey.Equals(BreadLot()));
            Assert.AreEqual("carry-12-1", result.BatchId);
            Assert.IsTrue(authority.IsFree(HandSlot.Left));
            Assert.IsTrue(!authority.IsFree(HandSlot.Right));
            Assert.IsTrue(authority.Current.RightLotKey.Equals(FlourLot()));
        }

        [Test]
        public void ReleaseTwoHandedHold_FreesBothHandsInOneOperation()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            authority.AllocateTwoHanded(ToolLot(), "carry-12-3", HandRequirement.TwoHanded);

            HandOperationResult result = authority.Release(HandSlot.Left);

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(result.LotKey.Equals(ToolLot()));
            Assert.AreEqual(2, authority.FreeHandCount);
            Assert.IsTrue(authority.Current.LeftLotKey.IsEmpty);
            Assert.IsTrue(authority.Current.RightLotKey.IsEmpty);
            Assert.IsTrue(!authority.Current.IsTwoHandedHold);
        }

        [Test]
        public void ReleaseEmptyHand_Refused()
        {
            var authority = new HandOccupancyAuthority(PlayerId);

            HandOperationResult result = authority.Release(HandSlot.Right);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Reason.Contains("right hand is already empty"), result.Reason);
            Assert.AreEqual(2, authority.FreeHandCount);
        }

        [Test]
        public void AllocateAfterRelease_Succeeds()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            authority.Release(HandSlot.Left);

            HandOperationResult result = authority.AllocateOneHanded(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Left);

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(FlourLot()));
            Assert.AreEqual("carry-12-2", authority.Current.LeftBatchId);
        }

        [Test]
        public void TwoDifferentLotsInTwoHands_Allowed()
        {
            var authority = new HandOccupancyAuthority(PlayerId);

            HandOperationResult left = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            HandOperationResult right = authority.AllocateOneHanded(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Right);

            Assert.IsTrue(left.Succeeded, left.Reason);
            Assert.IsTrue(right.Succeeded, right.Reason);
            Assert.AreEqual(0, authority.FreeHandCount);
            Assert.IsTrue(!authority.Current.IsTwoHandedHold);
        }

        [Test]
        public void SameLotInBothHands_TwoOneHandedAllocates_Refused()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);

            HandOperationResult result = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Reason.Contains("already held"), result.Reason);
            Assert.IsTrue(result.Reason.Contains("lot-bread-1"), result.Reason);
            Assert.IsTrue(authority.IsFree(HandSlot.Right));
            Assert.IsTrue(!authority.Current.IsTwoHandedHold);
        }

        [Test]
        public void PlayerAndNpcParity_SameRulesDifferentPersons()
        {
            var player = new HandOccupancyAuthority(PlayerId);
            var npc = new HandOccupancyAuthority(NpcId);

            HandOperationResult playerHold = player.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);
            HandOperationResult npcHold = npc.AllocateOneHanded(
                FlourLot(), "carry-12-2", HandRequirement.OneHanded, HandSlot.Left);
            HandOperationResult npcOccupied = npc.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);

            Assert.IsTrue(playerHold.Succeeded, playerHold.Reason);
            Assert.IsTrue(npcHold.Succeeded, npcHold.Reason);
            Assert.IsTrue(!npcOccupied.Succeeded);
            Assert.IsTrue(npcOccupied.Reason.Contains("left hand is occupied"), npcOccupied.Reason);
            Assert.IsTrue(player.Current.LeftLotKey.Equals(BreadLot()));
            Assert.IsTrue(npc.Current.LeftLotKey.Equals(FlourLot()));
            Assert.IsTrue(!player.HoldsLot(FlourLot()));
            Assert.IsTrue(!npc.HoldsLot(BreadLot()));
        }

        [Test]
        public void LotIdentityPreserved_ThroughAllocateAndReleaseByLotKey()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Right);

            HandOccupancy held = authority.Current;
            Assert.IsTrue(held.RightLotKey.Equals(BreadLot()));
            Assert.AreEqual("household-inventory", held.RightLotKey.AuthorityId);
            Assert.AreEqual("lot-bread-1", held.RightLotKey.LotId);

            HandOperationResult released = authority.Release(HandSlot.Right);
            Assert.IsTrue(released.Succeeded, released.Reason);
            Assert.IsTrue(released.LotKey.Equals(BreadLot()));
            Assert.AreEqual("carry-12-1", released.BatchId);
            Assert.IsTrue(!authority.HoldsLot(BreadLot()));
        }

        [Test]
        public void AllocateWithoutRequirement_RefusedNamingMissingRequirement()
        {
            var authority = new HandOccupancyAuthority(PlayerId);

            HandOperationResult noRequirement = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", null, HandSlot.Left);
            HandOperationResult unknownRequirement = authority.AllocateTwoHanded(
                ToolLot(), "carry-12-3", HandRequirement.Unknown);

            Assert.IsTrue(!noRequirement.Succeeded);
            Assert.IsTrue(noRequirement.Reason.Contains("no hand requirement was supplied"), noRequirement.Reason);
            Assert.IsTrue(!unknownRequirement.Succeeded);
            Assert.IsTrue(unknownRequirement.Reason.Contains("no hand requirement was supplied"), unknownRequirement.Reason);
            Assert.AreEqual(2, authority.FreeHandCount);
        }

        [Test]
        public void AllocateWithMismatchedRequirement_RefusedNamingCorrectAllocate()
        {
            var authority = new HandOccupancyAuthority(PlayerId);

            HandOperationResult twoHandedAsOne = authority.AllocateOneHanded(
                ToolLot(), "carry-12-3", HandRequirement.TwoHanded, HandSlot.Left);
            HandOperationResult oneHandedAsTwo = authority.AllocateTwoHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded);
            HandOperationResult noHandCarryable = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.NoHandCarryable, HandSlot.Left);

            Assert.IsTrue(!twoHandedAsOne.Succeeded);
            Assert.IsTrue(twoHandedAsOne.Reason.Contains("requires two hands"), twoHandedAsOne.Reason);
            Assert.IsTrue(!oneHandedAsTwo.Succeeded);
            Assert.IsTrue(oneHandedAsTwo.Reason.Contains("requires one hand"), oneHandedAsTwo.Reason);
            Assert.IsTrue(!noHandCarryable.Succeeded);
            Assert.IsTrue(noHandCarryable.Reason.Contains("cannot be carried in a hand"), noHandCarryable.Reason);
            Assert.AreEqual(2, authority.FreeHandCount);
        }

        [Test]
        public void AllocateEmptyLot_Refused()
        {
            var authority = new HandOccupancyAuthority(PlayerId);

            HandOperationResult result = authority.AllocateOneHanded(
                LotKey.Empty, string.Empty, HandRequirement.OneHanded, HandSlot.Left);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Reason.Contains("no lot was supplied"), result.Reason);
        }

        [Test]
        public void TransferToContainer_UnwornSatchel_RefusedNamingUnworn()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var satchel = new WornContainer("satchel-1", WornContainerKind.Satchel);
            authority.AllocateOneHanded(BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);

            HandOperationResult result = authority.TransferToContainer(HandSlot.Left, satchel);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Reason.Contains("unworn"), result.Reason);
            Assert.IsTrue(result.Reason.Contains("satchel-1"), result.Reason);
            Assert.IsTrue(authority.Current.LeftLotKey.Equals(BreadLot()));
        }

        [Test]
        public void TransferToContainer_EmptyHand_RefusedNamingEmptyHand()
        {
            var authority = new HandOccupancyAuthority(PlayerId);
            var satchel = new WornContainer("satchel-1", WornContainerKind.Satchel);

            HandOperationResult result = authority.TransferToContainer(HandSlot.Right, satchel);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Reason.Contains("right hand is empty"), result.Reason);
        }

        [Test]
        public void LogRecording_AllocateAndReleaseRecordHandCausesOnlyWhenLogAndContextSupplied()
        {
            var log = new CustodyTransferLog();
            var authority = new HandOccupancyAuthority(PlayerId, log);

            HandOperationResult held = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("hand-test-pickup-1"));
            HandOperationResult released = authority.Release(HandSlot.Left, Context("hand-test-release-1"));

            Assert.IsTrue(held.Succeeded, held.Reason);
            Assert.IsTrue(released.Succeeded, released.Reason);
            Assert.AreEqual(2, log.Count);
            CustodyTransferRecord pickup = log.LatestForCause(CustodyTransferLog.CauseHandPickup, "hand-test-pickup-1");
            Assert.IsNotNull(pickup);
            Assert.IsTrue(pickup.DestinationLotKey.Equals(BreadLot()));
            Assert.AreEqual("person:101", pickup.ToCustodian);
            CustodyTransferRecord release = log.LatestForCause(CustodyTransferLog.CauseHandRelease, "hand-test-release-1");
            Assert.IsNotNull(release);
            Assert.IsTrue(release.SourceLotKey.Equals(BreadLot()));
        }

        [Test]
        public void NoLogSupplied_OperationsCompleteWithoutRecording()
        {
            var authority = new HandOccupancyAuthority(NpcId);

            HandOperationResult held = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left,
                Context("hand-test-pickup-2"));
            HandOperationResult released = authority.Release(HandSlot.Left, Context("hand-test-release-2"));

            Assert.IsTrue(held.Succeeded, held.Reason);
            Assert.IsTrue(released.Succeeded, released.Reason);
            Assert.AreEqual(2, authority.FreeHandCount);
        }

        [Test]
        public void LogSuppliedWithoutContext_OperationsCompleteWithoutRecording()
        {
            var log = new CustodyTransferLog();
            var authority = new HandOccupancyAuthority(PlayerId, log);

            HandOperationResult held = authority.AllocateOneHanded(
                BreadLot(), "carry-12-1", HandRequirement.OneHanded, HandSlot.Left);

            Assert.IsTrue(held.Succeeded, held.Reason);
            Assert.AreEqual(0, log.Count);
        }
    }
}
