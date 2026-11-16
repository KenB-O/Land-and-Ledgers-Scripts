using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>CAMPAIGN-B (DEF-042), package 2: counter service bypassed in
    /// dev builds. The Roslyn harness pins the one half it can reach — the
    /// owner-occupied clerk slot parses: "owner:{personId}" resolves to the
    /// owner's person id through WorkerSlotPersonId.TryParse (the rule the
    /// manager's clerk scan uses). Before the fix the same id was
    /// unparseable, so the NPC store's owner-occupied clerk slot resolved
    /// to no clerk and Slice 17/18b/19 never engaged.
    ///
    /// The other halves are covered where they can run, not here:
    /// - the clerk scan itself (paid-active clerk_helper slot iteration) is
    ///   pinned by the owner-slot tests added to
    ///   GeneralStoreRuntimeManagerTests.cs (Unity-only file — the manager
    ///   cannot compile in the Roslyn harness, see that file's header);
    /// - the service path engaging for the NPC store id is covered by the
    ///   existing Slice 16/17/18b tests in ShoppingTripDriverTests.cs
    ///   (Unity), which wire the same resolvers;
    /// - the SimulationDrivers resolver lambdas (player id vs NPC store id
    ///   matching) cannot compile in the harness and are verified by source
    ///   inspection plus a Unity run (Codex).
    ///
    /// A loop-level test was attempted for this file and dropped: pulling
    /// HouseholdShoppingLoop + the real BusinessRuntimeState into the
    /// focused harness explodes the closure (433 files, 1690 errors, no
    /// convergence — the real BusinessRuntimeState.cs is deliberately
    /// stubbed out of this harness, see Shims/CampaignBStubs.cs).</summary>
    public sealed class CampaignBCounterServiceTests
    {
        [Test]
        public void OwnerClerkSlot_ResolvesToOwnerPersonId()
        {
            Assert.IsTrue(WorkerSlotPersonId.TryParse("owner:1", out int personId));
            Assert.AreEqual(1, personId);
        }

        [Test]
        public void PlainIntegerClerkId_StillResolves()
        {
            Assert.IsTrue(WorkerSlotPersonId.TryParse("4", out int personId));
            Assert.AreEqual(4, personId);
        }

        [Test]
        public void OwnerSlotByDisplayName_DoesNotResolve()
        {
            // An owner recorded by name has no person id; inventing one
            // would assign service to the wrong person (or nobody).
            Assert.IsFalse(WorkerSlotPersonId.TryParse("owner:Emma", out _));
        }

        [Test]
        public void UnparseableClerkId_DoesNotResolve()
        {
            Assert.IsFalse(WorkerSlotPersonId.TryParse("not-a-number", out _));
        }

        [Test]
        public void NegativeClerkId_DoesNotResolve()
        {
            Assert.IsFalse(WorkerSlotPersonId.TryParse("owner:-1", out _));
            Assert.IsFalse(WorkerSlotPersonId.TryParse("-1", out _));
        }

        [Test]
        public void EmptyClerkId_DoesNotResolve()
        {
            Assert.IsFalse(WorkerSlotPersonId.TryParse("", out _));
            Assert.IsFalse(WorkerSlotPersonId.TryParse(null, out _));
        }
    }
}
