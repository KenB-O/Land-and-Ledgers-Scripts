using LandLedgers.Economy;
using LandLedgers.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class BusinessDelegationPolicyTests
    {
        private const float Epsilon = 0.0001f;

        [Test]
        public void PlayerOwnedBusinessesDefaultToDirectControl()
        {
            BusinessProfileDefinition profile = LoadProfile(BusinessType.GeneralStore);

            BusinessInstanceState business = BusinessInstanceState.Create(
                "test_player_store",
                profile,
                0,
                BusinessOwnerIdentity.Player());

            Assert.AreEqual(BusinessControlState.PlayerManaged, business.ControlState);
            Assert.AreEqual(ManagerPolicyPreset.StabilityFirst, business.ManagerPolicy);
            Assert.AreEqual(0.25f, ManagerPolicyEffects.CalculateReorderThreshold01(0.25f, business.ControlState, ManagerPolicyPreset.ProfitFirst), Epsilon);
            Assert.AreEqual(8000, ManagerPolicyEffects.CalculateReorderBudgetCents(8000, business.ControlState, ManagerPolicyPreset.ProfitFirst));
            Assert.AreEqual(0f, ManagerPolicyEffects.CalculateMarginAdjustment01(business.ControlState, ManagerPolicyPreset.ProfitFirst), Epsilon);
        }

        [Test]
        public void NonPlayerBusinessesDefaultToManagerRun()
        {
            BusinessProfileDefinition profile = LoadProfile(BusinessType.Blacksmith);

            BusinessInstanceState business = BusinessInstanceState.Create(
                "test_npc_blacksmith",
                profile,
                1,
                BusinessOwnerIdentity.Npc(123, "Test Smith", "Smith"));

            Assert.AreEqual(BusinessControlState.ManagerRun, business.ControlState);
            Assert.AreEqual(ManagerPolicyPreset.StabilityFirst, business.ManagerPolicy);
        }

        [Test]
        public void AssistedAppliesHalfStrengthAndManagerRunAppliesFullStrength()
        {
            Assert.AreEqual(0.30f, ManagerPolicyEffects.CalculateReorderThreshold01(
                0.25f,
                BusinessControlState.Assisted,
                ManagerPolicyPreset.StabilityFirst), Epsilon);
            Assert.AreEqual(0.35f, ManagerPolicyEffects.CalculateReorderThreshold01(
                0.25f,
                BusinessControlState.ManagerRun,
                ManagerPolicyPreset.StabilityFirst), Epsilon);
            Assert.AreEqual(0.20f, ManagerPolicyEffects.CalculateReorderThreshold01(
                0.25f,
                BusinessControlState.ManagerRun,
                ManagerPolicyPreset.ProfitFirst), Epsilon);
            Assert.AreEqual(8800, ManagerPolicyEffects.CalculateReorderBudgetCents(
                8000,
                BusinessControlState.Assisted,
                ManagerPolicyPreset.StabilityFirst));
            Assert.AreEqual(6800, ManagerPolicyEffects.CalculateReorderBudgetCents(
                8000,
                BusinessControlState.ManagerRun,
                ManagerPolicyPreset.ProfitFirst));
            Assert.AreEqual(12500, ManagerPolicyEffects.CalculateCashReserveCents(
                10000,
                BusinessControlState.ManagerRun,
                ManagerPolicyPreset.StabilityFirst));
            Assert.AreEqual(0.015f, ManagerPolicyEffects.CalculateMarginAdjustment01(
                BusinessControlState.Assisted,
                ManagerPolicyPreset.ProfitFirst), Epsilon);
            Assert.AreEqual(-0.03f, ManagerPolicyEffects.CalculateMarginAdjustment01(
                BusinessControlState.ManagerRun,
                ManagerPolicyPreset.ReputationFirst), Epsilon);
        }

        [Test]
        public void BusinessCanCycleControlAndPolicyWithoutOwnershipChanges()
        {
            BusinessProfileDefinition profile = LoadProfile(BusinessType.Butcher);
            BusinessInstanceState business = BusinessInstanceState.Create(
                "test_player_butcher",
                profile,
                2,
                BusinessOwnerIdentity.Player());

            Assert.AreEqual(BusinessControlState.Assisted, business.CycleControlState());
            Assert.AreEqual(BusinessControlState.ManagerRun, business.CycleControlState());
            Assert.AreEqual(BusinessControlState.PlayerManaged, business.CycleControlState());
            Assert.AreEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);

            Assert.AreEqual(ManagerPolicyPreset.ProfitFirst, business.CycleManagerPolicy());
            Assert.AreEqual(ManagerPolicyPreset.ReputationFirst, business.CycleManagerPolicy());
            Assert.AreEqual(ManagerPolicyPreset.StabilityFirst, business.CycleManagerPolicy());
        }

        [Test]
        public void DelegationStatePersistsThroughBusinessSaveDto()
        {
            BusinessProfileDefinition profile = LoadProfile(BusinessType.CropFarm);
            BusinessInstanceState source = BusinessInstanceState.Create(
                "test_player_crop_farm",
                profile,
                3,
                BusinessOwnerIdentity.Player());
            source.SetControlState(BusinessControlState.Assisted);
            source.SetManagerPolicy(ManagerPolicyPreset.ProfitFirst);

            BusinessInstanceSaveDto dto = source.CaptureSaveDto();
            BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(dto);

            Assert.AreEqual(BusinessControlState.Assisted, dto.controlState);
            Assert.AreEqual(ManagerPolicyPreset.ProfitFirst, dto.managerPolicy);
            Assert.NotNull(restored);
            Assert.AreEqual(BusinessControlState.Assisted, restored.ControlState);
            Assert.AreEqual(ManagerPolicyPreset.ProfitFirst, restored.ManagerPolicy);
            Assert.AreEqual(BusinessOwnerKind.Player, restored.Owner.OwnerKind);
        }

        [Test]
        public void PolicyEffectLineExplainsInactiveAndManagedBehavior()
        {
            string inactive = ManagerPolicyEffects.BuildPolicyEffectLine(
                BusinessControlState.PlayerManaged,
                ManagerPolicyPreset.ProfitFirst);
            string managed = ManagerPolicyEffects.BuildPolicyEffectLine(
                BusinessControlState.ManagerRun,
                ManagerPolicyPreset.ReputationFirst);

            StringAssert.Contains("no automatic effect", inactive);
            StringAssert.Contains("earlier reorders", managed);
            StringAssert.Contains("softer prices", managed);
        }

        private static BusinessProfileDefinition LoadProfile(BusinessType businessType)
        {
            BusinessProfileDefinition[] profiles = Resources.LoadAll<BusinessProfileDefinition>("Core/Economy/BusinessProfiles");
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] != null && profiles[i].Business.BusinessType == businessType)
                {
                    return profiles[i];
                }
            }

            Assert.Fail($"{businessType} profile should be present in Resources.");
            return null;
        }
    }
}
