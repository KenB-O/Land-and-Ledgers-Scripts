using LandLedgers.Economy;
using LandLedgers.Persistence;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Bootstrap
{
    /// <summary>
    /// Pass XII Worker A (LL-PLAY-001): player ownership must come from an
    /// explicit grant — formation, acquisition, or TransferToPlayerOwnership
    /// — never from an absent owner. Production creation always passes an
    /// explicit owner to Create (null resolves to Town, never Player), and
    /// every save round-trip preserves the owner (a missing owner DTO
    /// resolves to Town). These tests use bare state plus explicit
    /// SetOwner calls, mirroring Create's explicit-owner contract without
    /// needing a profile definition, so they run in the Roslyn harness.
    ///
    /// Known residual, NOT fixed by Worker A because the field lives
    /// outside Worker A's folders (Domains/Economy/Core, Worker B/C
    /// territory): BusinessInstanceState's serialized owner field still
    /// initializes to Player(), and BusinessOwnerKind.Player is the zero
    /// enum value, so a state materialized without Create or FromSaveDto
    /// reads as player-owned. No production caller uses bare construction
    /// (verified: all production creation goes through Create), and the
    /// save path is safe (FromSaveDto(null owner) resolves to Town), but
    /// the coordinator should still flip that field default to Town() —
    /// the enum order itself must NOT change (serialized append-only).
    /// These tests pin every safe path so that fix cannot regress them.
    /// </summary>
    [TestFixture]
    public sealed class Pass12BusinessOwnershipTests
    {
        private static BusinessInstanceState BuildBusiness(BusinessOwnerIdentity owner)
        {
            var business = new BusinessInstanceState();
            business.SetOwner(owner);
            return business;
        }

        [Test]
        public void ExplicitNpcOwner_IsNpcOwned_NeverPlayerOwned()
        {
            BusinessInstanceState business = BuildBusiness(BusinessOwnerIdentity.Npc(7, "Ada", "Hart"));
            Assert.AreEqual(BusinessOwnerKind.Npc, business.Owner.OwnerKind);
            Assert.AreEqual(7, business.Owner.PersonId);
            Assert.AreNotEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);
        }

        [Test]
        public void ExplicitPlayerOwner_IsPlayerOwned_ByExplicitGrantOnly()
        {
            BusinessInstanceState business = BuildBusiness(BusinessOwnerIdentity.Player());
            Assert.AreEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);
        }

        [Test]
        public void SetOwner_Null_FallsBackToTown_NeverPlayer()
        {
            BusinessInstanceState business = BuildBusiness(BusinessOwnerIdentity.Player());
            business.SetOwner(null);
            Assert.AreEqual(BusinessOwnerKind.Town, business.Owner.OwnerKind);
        }

        [Test]
        public void TransferToPlayerOwnership_IsTheExplicitPlayerGrant()
        {
            BusinessInstanceState business = BuildBusiness(BusinessOwnerIdentity.Npc(7, "Ada", "Hart"));
            business.TransferToPlayerOwnership();
            Assert.AreEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);
        }

        [Test]
        public void OwnerIdentity_FromSaveDto_NullResolvesToTown()
        {
            Assert.AreEqual(BusinessOwnerKind.Town, BusinessOwnerIdentity.FromSaveDto(null).OwnerKind);
        }

        [Test]
        public void SaveRoundTrip_PreservesOwner_AndMissingOwnerResolvesToTown()
        {
            // Bare-new states carry no instance id (production Create
            // assigns one); the DTO round-trip under test is the owner
            // field, so the test supplies the identity a Create call
            // would have given.
            BusinessInstanceState npcBusiness = BuildBusiness(BusinessOwnerIdentity.Npc(7, "Ada", "Hart"));
            BusinessInstanceSaveDto npcDto = npcBusiness.CaptureSaveDto();
            npcDto.instanceId = "test_business";
            BusinessInstanceState restoredNpc = BusinessInstanceState.FromSaveDto(npcDto);
            Assert.IsNotNull(restoredNpc);
            Assert.AreEqual(BusinessOwnerKind.Npc, restoredNpc.Owner.OwnerKind);
            Assert.AreEqual(7, restoredNpc.Owner.PersonId);

            BusinessInstanceState playerBusiness = BuildBusiness(BusinessOwnerIdentity.Player());
            BusinessInstanceSaveDto playerDto = playerBusiness.CaptureSaveDto();
            playerDto.instanceId = "test_business";
            BusinessInstanceState restoredPlayer = BusinessInstanceState.FromSaveDto(playerDto);
            Assert.AreEqual(BusinessOwnerKind.Player, restoredPlayer.Owner.OwnerKind);

            BusinessInstanceSaveDto dtoMissingOwner = npcBusiness.CaptureSaveDto();
            dtoMissingOwner.instanceId = "test_business";
            dtoMissingOwner.owner = null;
            BusinessInstanceState restoredMissing = BusinessInstanceState.FromSaveDto(dtoMissingOwner);
            Assert.IsNotNull(restoredMissing);
            Assert.AreEqual(BusinessOwnerKind.Town, restoredMissing.Owner.OwnerKind,
                "a save record with no owner must never load as player-owned");
        }

        [Test]
        public void FromSaveDto_NullDto_ReturnsNull_NeverAPhantomBusiness()
        {
            Assert.IsNull(BusinessInstanceState.FromSaveDto(null));
        }

        [Test]
        public void FromSaveDto_MissingInstanceId_ReturnsNull()
        {
            var dto = new BusinessInstanceSaveDto { instanceId = "   " };
            Assert.IsNull(BusinessInstanceState.FromSaveDto(dto));
        }
    }
}
