using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Market;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    public sealed class SettlementFoundationReconciliationTests
    {
        [Test]
        public void ValidPeopleHouseholdsAndBusinessOwnerPassWithoutRepair()
        {
            var population = new PopulationState();
            var household = new HouseholdState { id = population.AllocateNextHouseholdId(), homeBuildingId = 12 };
            var person = new PersonState
            {
                id = population.AllocateNextPersonId(),
                householdId = household.id,
                homeBuildingId = household.homeBuildingId,
                age = 34,
                lastName = "Collins",
                firstName = "Martha",
                settlementArrangement = SettlementArrangement.StableHousehold
            };
            household.memberIds.Add(person.id);
            population.people.Add(person);
            population.households.Add(household);

            BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(BusinessType.GeneralStore, "Store");
            BusinessInstanceState business = BusinessInstanceState.Create(
                "store-1", profile, 21, BusinessOwnerIdentity.Npc(person.id, person.DisplayName, person.lastName));

            SettlementFoundationReconciliation result = SettlementFoundationReconciliation.Check(
                population, new[] { business });

            Assert.IsTrue(result.IsValid, result.BuildSummary());
        }

        [Test]
        public void MissingOwnerAndAccommodationAreReportedWithoutFabrication()
        {
            var population = new PopulationState();
            var household = new HouseholdState { id = population.AllocateNextHouseholdId() };
            var person = new PersonState
            {
                id = population.AllocateNextPersonId(),
                householdId = household.id,
                homeBuildingId = -1,
                settlementArrangement = SettlementArrangement.StableHousehold
            };
            household.memberIds.Add(person.id);
            population.people.Add(person);
            population.households.Add(household);

            BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(BusinessType.GeneralStore, "Store");
            BusinessInstanceState business = BusinessInstanceState.Create(
                "store-2", profile, 21, BusinessOwnerIdentity.Npc(999, "Missing", "Missing"));

            SettlementFoundationReconciliation result = SettlementFoundationReconciliation.Check(
                population, new List<BusinessInstanceState> { business });

            Assert.IsFalse(result.IsValid);
            Assert.That(result.Errors, Has.Some.Contains("no accommodation"));
            Assert.That(result.Errors, Has.Some.Contains("missing owner Person 999"));
        }

        [Test]
        public void OpeningFormationIntentCanCarryARealPersonOwner()
        {
            OpeningRosterEntry entry = new OpeningRosterEntry(
                BusinessType.LiveryFreight,
                "Collins Freight",
                "Town",
                new PremisesRequirement(yardStorage: true));

            CreateBusinessIntent intent = entry.ToIntent(
                BusinessOwnerIdentity.Npc(17, "Martha Collins", "Collins"));

            Assert.AreEqual(BusinessOwnerKind.Npc, intent.Ownership.Shares[0].Owner.OwnerKind);
            Assert.AreEqual(17, intent.Ownership.Shares[0].Owner.PersonId);
            Assert.AreEqual("Collins Freight", intent.DisplayName);
        }
    }
}
