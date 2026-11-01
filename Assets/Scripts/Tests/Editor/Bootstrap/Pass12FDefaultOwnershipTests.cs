using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Bootstrap
{
    /// <summary>
    /// Pass XII Worker F (M2, LL-PLAY-001): a BusinessInstanceState
    /// materialized without Create or FromSaveDto must never read as
    /// player-owned. The serialized owner field previously initialized
    /// to Player(), so bare/default construction — including Unity
    /// deserialization of a scene-embedded instance with no stored
    /// owner — invented player ownership. The field default is now
    /// Town(); every production creation path still sets the owner
    /// explicitly (Create resolves null to Town, FromSaveDto resolves a
    /// missing owner DTO to Town, SetOwner(null) resolves to Town),
    /// and an explicit player grant still works. The enum order is
    /// unchanged (serialized append-only). Caveat, not covered here:
    /// a scene instance already serialized with a Player owner keeps
    /// that stored value; Codex must audit scene instances.
    /// </summary>
    [TestFixture]
    public sealed class Pass12FDefaultOwnershipTests
    {
        [Test]
        public void BareNewState_DefaultsToTown_NeverPlayer()
        {
            var business = new BusinessInstanceState();
            Assert.AreEqual(BusinessOwnerKind.Town, business.Owner.OwnerKind);
            Assert.AreNotEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);
        }

        [Test]
        public void BareNewState_ControlStateReadsManagerRun_ForTownOwner()
        {
            var business = new BusinessInstanceState();
            Assert.AreEqual(BusinessOwnerKind.Town, business.Owner.OwnerKind);
            Assert.AreEqual(BusinessControlState.ManagerRun, business.ControlState,
                "The ControlState getter sanitizes against the owner: a Town-owned business must not read as PlayerManaged.");
        }

        [Test]
        public void ExplicitPlayerGrant_StillWorks_AfterDefaultChange()
        {
            var business = new BusinessInstanceState();
            business.SetOwner(BusinessOwnerIdentity.Player());
            Assert.AreEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);
            business.TransferToPlayerOwnership();
            Assert.AreEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);
        }

        [Test]
        public void SetOwner_Null_StillFallsBackToTown()
        {
            var business = new BusinessInstanceState();
            business.SetOwner(null);
            Assert.AreEqual(BusinessOwnerKind.Town, business.Owner.OwnerKind);
        }
    }
}
