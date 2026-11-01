using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Phase D (Real People): residential conditions and shelter suitability.
    /// Every Person gets a valid current accommodation relationship OR an
    /// explicit temporary/precarious/unsheltered condition. SHELTER
    /// SUITABILITY MATTERS: a vacant bunk does not satisfy a family needing
    /// multiple sleeping places — capacity requirements scale with household
    /// composition. Person != Household != Residential Occupancy !=
    /// Accommodation Space != Building != Parcel.
    /// </summary>
    [TestFixture]
    public sealed class HousingResidenceTests
    {
        private HousingAuthority NewHousing()
        {
            return new HousingAuthority();
        }

        private List<PersonState> NewMembers(params (int id, int age)[] defs)
        {
            var list = new List<PersonState>();
            foreach (var def in defs)
                list.Add(new PersonState { id = def.id, age = def.age, householdId = 1, deathDayIndex = -1 });
            return list;
        }

        private AccommodationSpace MakeSpace(HousingAuthority housing, List<string> diag, int capacity)
        {
            housing.RegisterBuilding("b-1", "p-1", "house", 0, "test fixture", diag);
            return housing.DefineSpace("b-1", capacity, diag);
        }

        [Test]
        public void PersonWithoutOccupancyGetsExplicitUnshelteredCondition()
        {
            var housing = NewHousing();
            var registry = new ResidentialConditionRegistry();
            var diag = new List<string>();

            ResidentialConditionRecord record = registry.EnsurePersonCondition(7, 1, housing, 10, diag);

            Assert.IsNotNull(record);
            Assert.AreEqual(ResidentialConditionKind.Unsheltered, record.Condition);
            Assert.AreEqual(7, record.PersonId);
            Assert.AreEqual(1, record.HouseholdId);
            Assert.IsTrue(record.IsCurrent);
            Assert.AreEqual(record, registry.CurrentConditionFor(7));
        }

        [Test]
        public void PersonWithCurrentOccupancyHasNoConditionRecord()
        {
            var housing = NewHousing();
            var registry = new ResidentialConditionRegistry();
            var diag = new List<string>();
            AccommodationSpace space = MakeSpace(housing, diag, 2);
            housing.Occupy(7, 1, space.SpaceId, AccommodationArrangement.BoardingOrLodging, 5, diag);

            ResidentialConditionRecord record = registry.EnsurePersonCondition(7, 1, housing, 10, diag);

            Assert.IsNull(record);
            Assert.IsNull(registry.CurrentConditionFor(7));
        }

        [Test]
        public void ConditionClosesWhenOccupancyIsEstablished()
        {
            var housing = NewHousing();
            var registry = new ResidentialConditionRegistry();
            var diag = new List<string>();
            registry.EnsurePersonCondition(7, 1, housing, 10, diag);
            Assert.IsNotNull(registry.CurrentConditionFor(7));

            AccommodationSpace space = MakeSpace(housing, diag, 2);
            housing.Occupy(7, 1, space.SpaceId, AccommodationArrangement.Rental, 12, diag);
            registry.EnsurePersonCondition(7, 1, housing, 12, diag);

            Assert.IsNull(registry.CurrentConditionFor(7));
            Assert.AreEqual(1, registry.History.Count);
            Assert.IsFalse(registry.History[0].IsCurrent);
        }

        [Test]
        public void SuitabilityGatingBunkDoesNotSatisfyFamily()
        {
            var housing = NewHousing();
            var diag = new List<string>();
            // A family of four: 2 adults + 2 children -> 2 + 1 = 3 sleeping places required.
            List<PersonState> members = NewMembers((1, 34), (2, 31), (3, 9), (4, 6));
            AccommodationSpace bunk = MakeSpace(housing, diag, 1); // one vacant bunk
            housing.Occupy(1, 1, bunk.SpaceId, AccommodationArrangement.BoardingOrLodging, 5, diag);
            housing.Occupy(2, 1, bunk.SpaceId, AccommodationArrangement.BoardingOrLodging, 5, diag);
            housing.Occupy(3, 1, bunk.SpaceId, AccommodationArrangement.BoardingOrLodging, 5, diag);
            housing.Occupy(4, 1, bunk.SpaceId, AccommodationArrangement.BoardingOrLodging, 5, diag);

            var evaluator = new HouseholdShelterSuitabilityEvaluator();
            HouseholdShelterSuitability verdict = evaluator.Evaluate(1, members, housing, diag);

            Assert.AreEqual(3, verdict.Composition.RequiredSleepingPlaces);
            Assert.AreEqual(1, verdict.ProvidedSleepingPlaces);
            Assert.IsFalse(verdict.IsSuitable);
            Assert.AreEqual(2, verdict.GapPlaces);
            Assert.AreEqual(0, verdict.UncoveredPersonIds.Count); // all are housed, just unsuitably
        }

        [Test]
        public void SuitabilityGatingAdequateSpaceIsSuitable()
        {
            var housing = NewHousing();
            var diag = new List<string>();
            List<PersonState> members = NewMembers((1, 34), (2, 31), (3, 9), (4, 6));
            AccommodationSpace room = MakeSpace(housing, diag, 3);
            housing.Occupy(1, 1, room.SpaceId, AccommodationArrangement.Rental, 5, diag);
            housing.Occupy(2, 1, room.SpaceId, AccommodationArrangement.Rental, 5, diag);
            housing.Occupy(3, 1, room.SpaceId, AccommodationArrangement.Rental, 5, diag);
            housing.Occupy(4, 1, room.SpaceId, AccommodationArrangement.Rental, 5, diag);

            var evaluator = new HouseholdShelterSuitabilityEvaluator();
            HouseholdShelterSuitability verdict = evaluator.Evaluate(1, members, housing, diag);

            Assert.IsTrue(verdict.IsSuitable);
            Assert.AreEqual(0, verdict.GapPlaces);
        }

        [Test]
        public void PersonsWithoutOccupancyAreListedAsUncovered()
        {
            var housing = NewHousing();
            var diag = new List<string>();
            // One adult housed in a bunk; the spouse has no occupancy at all.
            List<PersonState> members = NewMembers((1, 40), (2, 38));
            AccommodationSpace bunk = MakeSpace(housing, diag, 1);
            housing.Occupy(1, 1, bunk.SpaceId, AccommodationArrangement.TemporaryCamp, 5, diag);

            var evaluator = new HouseholdShelterSuitabilityEvaluator();
            HouseholdShelterSuitability verdict = evaluator.Evaluate(1, members, housing, diag);

            Assert.AreEqual(2, verdict.Composition.RequiredSleepingPlaces);
            Assert.AreEqual(1, verdict.ProvidedSleepingPlaces);
            Assert.IsFalse(verdict.IsSuitable);
            Assert.AreEqual(1, verdict.UncoveredPersonIds.Count);
            Assert.AreEqual(2, verdict.UncoveredPersonIds[0]);
        }

        [Test]
        public void SaveLoadRoundTripHasNoDuplicatedRecords()
        {
            var housing = NewHousing();
            var diag = new List<string>();
            AccommodationSpace space = MakeSpace(housing, diag, 2);
            housing.Occupy(7, 1, space.SpaceId, AccommodationArrangement.Rental, 5, diag);

            var registry = new ResidentialConditionRegistry();
            registry.EnsurePersonCondition(9, 2, housing, 10, diag);
            ResidentialConditionRegistry.ResidentialConditionRegistrySaveDto dto = registry.CaptureSaveDto();

            var reloaded = new ResidentialConditionRegistry();
            reloaded.LoadFromSaveDto(dto);
            // Re-importing the same DTO must not duplicate people or records.
            reloaded.LoadFromSaveDto(dto);
            reloaded.LoadFromSaveDto(dto);

            Assert.AreEqual(1, reloaded.History.Count);
            Assert.AreEqual(9, reloaded.History[0].PersonId);
            Assert.IsNotNull(reloaded.CurrentConditionFor(9));
        }

        [Test]
        public void DuplicateOpenConditionsInSaveAreQuarantined()
        {
            var dto = new ResidentialConditionRegistry.ResidentialConditionRegistrySaveDto();
            dto.History.Add(new ResidentialConditionRecord
            {
                RecordId = "rcond-0", PersonId = 9, HouseholdId = 2,
                Condition = ResidentialConditionKind.Unsheltered, SinceDayIndex = 1, UntilDayIndex = -1,
            });
            dto.History.Add(new ResidentialConditionRecord
            {
                RecordId = "rcond-1", PersonId = 9, HouseholdId = 2,
                Condition = ResidentialConditionKind.PrecariousCondition, SinceDayIndex = 2, UntilDayIndex = -1,
            });

            var registry = new ResidentialConditionRegistry();
            registry.LoadFromSaveDto(dto);

            Assert.AreEqual(2, registry.History.Count);
            Assert.IsNotNull(registry.CurrentConditionFor(9));
            Assert.AreEqual("rcond-0", registry.CurrentConditionFor(9).RecordId);
        }
    }
}
