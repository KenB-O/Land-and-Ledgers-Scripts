using LandLedgers.Economy;
using LandLedgers.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// UBC I2 (F5): the development NPC General Store lookup must resolve the
    /// real store owner, never a hardcoded roster position. Person id 1 is
    /// whoever the opening roster lists first today; the lookup must keep
    /// working when the roster order changes.
    /// </summary>
    public sealed class DevelopmentNpcGeneralStoreTests
    {
        private static BusinessInstanceState BuildStore(BusinessType type, BusinessOwnerIdentity owner)
        {
            BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(type, type.ToString());
            return BusinessInstanceState.Create("test_general_store", profile, -1, owner);
        }

        [Test]
        public void IsDevelopmentNpcGeneralStore_NpcOwnerWithOtherPersonId_Matches()
        {
            BusinessInstanceState store = BuildStore(
                BusinessType.GeneralStore,
                BusinessOwnerIdentity.Npc(7, "Test", "Owner"));
            Assert.IsTrue(BusinessInstanceState.IsDevelopmentNpcGeneralStore(store));
        }

        [Test]
        public void IsDevelopmentNpcGeneralStore_PersonIdOne_Matches()
        {
            BusinessInstanceState store = BuildStore(
                BusinessType.GeneralStore,
                BusinessOwnerIdentity.Npc(1, "Kennedy", "Baldwin-Ooms"));
            Assert.IsTrue(BusinessInstanceState.IsDevelopmentNpcGeneralStore(store));
        }

        [Test]
        public void IsDevelopmentNpcGeneralStore_PlayerOwnedGeneralStore_DoesNotMatch()
        {
            BusinessInstanceState store = BuildStore(
                BusinessType.GeneralStore,
                BusinessOwnerIdentity.Player());
            Assert.IsTrue(!BusinessInstanceState.IsDevelopmentNpcGeneralStore(store));
        }

        [Test]
        public void IsDevelopmentNpcGeneralStore_NpcBakery_DoesNotMatch()
        {
            BusinessInstanceState store = BuildStore(
                BusinessType.Bakery,
                BusinessOwnerIdentity.Npc(3, "Glen", "Pierce"));
            Assert.IsTrue(!BusinessInstanceState.IsDevelopmentNpcGeneralStore(store));
        }

        [Test]
        public void IsDevelopmentNpcGeneralStore_Null_DoesNotMatch()
        {
            Assert.IsTrue(!BusinessInstanceState.IsDevelopmentNpcGeneralStore(null));
        }

        [Test]
        public void IsDevelopmentNpcGeneralStore_NoRuntime_DoesNotMatch()
        {
            var dto = new BusinessInstanceSaveDto
            {
                instanceId = "test_general_store_no_runtime",
                businessType = BusinessType.GeneralStore,
                owner = new BusinessOwnerSaveDto
                {
                    ownerKind = BusinessOwnerKind.Npc,
                    personId = 7,
                    displayName = "Test",
                    surname = "Owner"
                },
                runtime = null,
            };
            BusinessInstanceState store = BusinessInstanceState.FromSaveDto(dto);
            Assert.IsTrue(!BusinessInstanceState.IsDevelopmentNpcGeneralStore(store));
        }
    }
}
