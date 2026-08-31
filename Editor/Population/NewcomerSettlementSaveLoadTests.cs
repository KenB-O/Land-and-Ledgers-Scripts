using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    public sealed class NewcomerSettlementSaveLoadTests
    {
        [Test]
        public void PopulationSaveLoadRoundTripsNewcomerBoardingSettlementFields()
        {
            GameObject sourceObject = new("Settlement Save Source");
            GameObject targetObject = new("Settlement Save Target");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                PopulateSettlementState(source);

                PopulationSaveDto dto = source.CaptureSaveDto();
                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                PersonSaveDto expectedBoarder = FindPersonDto(dto, 101);
                PersonState restoredBoarder = target.State.GetPerson(101);
                AssertPersonSettlementMatches(expectedBoarder, restoredBoarder);

                HouseholdSaveDto expectedHousehold = FindHouseholdDto(dto, 10);
                HouseholdState restoredHousehold = target.State.GetHousehold(10);
                AssertHouseholdSettlementMatches(expectedHousehold, restoredHousehold);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PopulationSaveLoadRoundTripsSettlementAggregateMetrics()
        {
            GameObject sourceObject = new("Settlement Metric Source");
            GameObject targetObject = new("Settlement Metric Target");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                PopulateSettlementState(source);

                PopulationSaveDto dto = source.CaptureSaveDto();
                Assert.Greater(dto.boardingCapacity, 0);
                Assert.Greater(dto.boardingUsed, 0);
                Assert.Greater(dto.transientPersonCount, 0);
                Assert.IsFalse(string.IsNullOrWhiteSpace(dto.lastSettlementSummary));

                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                Assert.AreEqual(dto.boardingCapacity, target.State.boardingCapacity);
                Assert.AreEqual(dto.boardingUsed, target.State.boardingUsed);
                Assert.AreEqual(dto.renterHouseholdCount, target.State.renterHouseholdCount);
                Assert.AreEqual(dto.kinPlacementCount, target.State.kinPlacementCount);
                Assert.AreEqual(dto.transientPersonCount, target.State.transientPersonCount);
                Assert.AreEqual(dto.unstableHouseholdCount, target.State.unstableHouseholdCount);
                Assert.AreEqual(dto.departedUnsettledCount, target.State.departedUnsettledCount);
                Assert.That(target.State.housingPressure01, Is.EqualTo(dto.housingPressure01).Within(0.0001f));
                Assert.That(target.State.laborAbsorption01, Is.EqualTo(dto.laborAbsorption01).Within(0.0001f));
                Assert.AreEqual(dto.lastSettlementSummary, target.State.lastSettlementSummary);
                Assert.AreEqual(dto.lastSettlementSummary, target.LastSettlementSummary);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PopulationSaveLoadRoundTripsBoardingHouseGuestFields()
        {
            GameObject sourceObject = new("Boarding House Save Source");
            GameObject targetObject = new("Boarding House Save Target");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                source.State.people.Add(CreatePerson(
                    501,
                    50,
                    NewcomerArrivalProfile.BoarderSeekingWorker,
                    SettlementArrangement.Boarding,
                    50));
                source.State.households.Add(new HouseholdState
                {
                    id = 50,
                    householdName = "Depot House Guests",
                    surname = "Boarding",
                    homeBuildingId = 77,
                    memberIds = new List<int> { 501 },
                    boarderPersonIds = new List<int> { 501 },
                    arrivalProfile = NewcomerArrivalProfile.BoarderSeekingWorker,
                    settlementArrangement = SettlementArrangement.Boarding,
                    baseBoardingCapacity = 4,
                    boardingCapacity = 4,
                    hostsBoarders = true,
                    isBoardingHouseGuestHousehold = true,
                    lodgingBusinessInstanceId = "boarding_depot_01",
                    lastSettlementSummary = "Saved boarding house lodging"
                });

                PopulationSaveDto dto = source.CaptureSaveDto();
                Assert.AreEqual(4, dto.boardingHouseCapacity);
                Assert.AreEqual(1, dto.boardingHouseUsed);

                HouseholdSaveDto expectedHousehold = FindHouseholdDto(dto, 50);
                Assert.IsTrue(expectedHousehold.isBoardingHouseGuestHousehold);
                Assert.AreEqual("boarding_depot_01", expectedHousehold.lodgingBusinessInstanceId);
                Assert.IsTrue(expectedHousehold.isBoardingHouseLodging);
                Assert.AreEqual("boarding_depot_01", expectedHousehold.boardingBusinessInstanceId);

                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                HouseholdState restored = target.State.GetHousehold(50);
                Assert.NotNull(restored);
                Assert.IsTrue(restored.isBoardingHouseGuestHousehold);
                Assert.AreEqual("boarding_depot_01", restored.lodgingBusinessInstanceId);
                Assert.IsTrue(restored.isBoardingHouseLodging);
                Assert.AreEqual("boarding_depot_01", restored.boardingBusinessInstanceId);
                Assert.AreEqual(4, target.State.boardingHouseCapacity);
                Assert.AreEqual(1, target.State.boardingHouseUsed);
                CollectionAssert.AreEqual(new List<int> { 501 }, restored.boarderPersonIds);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PopulationSaveLoadRoundTripsRentalMetricsAndLists()
        {
            GameObject worldObject = new("Rental Metric World");
            GameObject sourceObject = new("Rental Metric Source");
            GameObject targetObject = new("Rental Metric Target");
            BuildingDefinition buildingDefinition = null;
            try
            {
                TownWorldController townWorld = worldObject.AddComponent<TownWorldController>();
                buildingDefinition = ScriptableObject.CreateInstance<BuildingDefinition>();
                AddBuildingToTownWorld(townWorld, buildingDefinition, 70, true);

                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                SetPrivateField(source, "townWorld", townWorld);
                source.State.rentalApplicants.Add(new RentalApplicantState
                {
                    personId = 601,
                    householdId = 60,
                    preferredBuildingId = 70,
                    displayName = "Ada Renter",
                    arrivalProfile = NewcomerArrivalProfile.RenterReadyHousehold,
                    currentArrangement = SettlementArrangement.Boarding,
                    savingsCents = 8400,
                    weeklyIncomeCents = 1600,
                    weeksWaiting = 3,
                    score01 = 0.73f,
                    acceptedLastWeek = false,
                    status = "Waiting on open room"
                });

                PopulationSaveDto dto = source.CaptureSaveDto();
                Assert.AreEqual(1, dto.rentalProperties.Count);
                Assert.AreEqual(1, dto.rentalApplicants.Count);
                Assert.AreEqual(1, dto.rentalVacancyCount);
                Assert.AreEqual(1, dto.playerOwnedRentalVacancyCount);
                Assert.AreEqual(1, dto.rentalApplicantCount);
                Assert.IsFalse(string.IsNullOrWhiteSpace(dto.lastRentalSummary));

                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                Assert.AreEqual(dto.rentalVacancyCount, target.State.rentalVacancyCount);
                Assert.AreEqual(dto.playerOwnedRentalVacancyCount, target.State.playerOwnedRentalVacancyCount);
                Assert.AreEqual(dto.rentalApplicantCount, target.State.rentalApplicantCount);
                Assert.AreEqual(dto.strongerHousingNeedCount, target.State.strongerHousingNeedCount);
                Assert.That(target.State.rentalPressure01, Is.EqualTo(dto.rentalPressure01).Within(0.0001f));
                Assert.AreEqual(dto.lastRentalSummary, target.State.lastRentalSummary);

                Assert.AreEqual(1, target.State.rentalProperties.Count);
                RentalPropertyState restoredProperty = target.State.rentalProperties[0];
                Assert.AreEqual(70, restoredProperty.buildingId);
                Assert.IsTrue(restoredProperty.playerOwned);
                Assert.AreEqual(1, restoredProperty.residentHouseholdCapacity);
                Assert.AreEqual(1, restoredProperty.vacantHouseholdSlots);

                Assert.AreEqual(1, target.State.rentalApplicants.Count);
                RentalApplicantState restoredApplicant = target.State.rentalApplicants[0];
                Assert.AreEqual(601, restoredApplicant.personId);
                Assert.AreEqual(60, restoredApplicant.householdId);
                Assert.AreEqual(70, restoredApplicant.preferredBuildingId);
                Assert.AreEqual(NewcomerArrivalProfile.RenterReadyHousehold, restoredApplicant.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.Boarding, restoredApplicant.currentArrangement);
                Assert.AreEqual(8400, restoredApplicant.savingsCents);
                Assert.AreEqual(3, restoredApplicant.weeksWaiting);
                Assert.That(restoredApplicant.score01, Is.EqualTo(0.73f).Within(0.0001f));
                Assert.AreEqual("Waiting on open room", restoredApplicant.status);
            }
            finally
            {
                if (buildingDefinition != null)
                {
                    Object.DestroyImmediate(buildingDefinition);
                }

                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PopulationLoadPreservesSavedZeroLaborAbsorptionMetric()
        {
            GameObject sourceObject = new("Settlement Zero Labor Source");
            GameObject targetObject = new("Settlement Zero Labor Target");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                PopulateSettlementState(source);

                PopulationSaveDto dto = source.CaptureSaveDto();
                dto.laborAbsorption01 = 0f;
                dto.lastSettlementSummary = "Saved no labor absorption.";

                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                Assert.That(target.State.laborAbsorption01, Is.EqualTo(0f).Within(0.0001f));
                Assert.AreEqual("Saved no labor absorption.", target.State.lastSettlementSummary);
                Assert.AreEqual("Saved no labor absorption.", target.LastSettlementSummary);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void LegacyPopulationLoadDefaultsMissingSettlementFields()
        {
            GameObject targetObject = new("Settlement Legacy Target");
            try
            {
                PopulationSaveDto legacyDto = new();
                legacyDto.people.Add(new PersonSaveDto
                {
                    id = 201,
                    firstName = "Legacy",
                    lastName = "Worker",
                    age = 28,
                    ageBand = AgeBand.Adult18Plus,
                    laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                    householdId = 20,
                    wage = WageSnapshot.None(),
                    homeBuildingId = 5,
                    workplaceBuildingId = -1,
                    scheduleState = PopulationScheduleState.AtHome,
                    currentDestinationBuildingId = 5
                });
                legacyDto.households.Add(new HouseholdSaveDto
                {
                    id = 20,
                    householdName = "Legacy Household",
                    surname = "Legacy",
                    homeBuildingId = 5,
                    memberIds = new List<int> { 201 }
                });

                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(legacyDto);

                PersonState person = target.State.GetPerson(201);
                Assert.AreEqual(NewcomerArrivalProfile.SettledResident, person.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.StableHousehold, person.settlementArrangement);
                Assert.IsTrue(person.settlementPressure.initialized);
                Assert.AreEqual("Stable household", person.settlementPressure.summary);
                Assert.AreEqual(0.45f, person.laborUrgency01);
                Assert.AreEqual(-1, person.hostHouseholdId);

                HouseholdState household = target.State.GetHousehold(20);
                Assert.AreEqual(NewcomerArrivalProfile.SettledResident, household.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.StableHousehold, household.settlementArrangement);
                Assert.IsTrue(household.settlementPressure.initialized);
                Assert.NotNull(household.boarderPersonIds);
                Assert.AreEqual(0, household.boarderPersonIds.Count);
                Assert.AreEqual(0, household.baseBoardingCapacity);
                Assert.AreEqual(0, household.boardingCapacity);
                Assert.IsFalse(household.hostsBoarders);
                Assert.IsFalse(household.isRenterHousehold);
                Assert.IsFalse(household.hasKinAbsorptionPressure);
                Assert.IsFalse(household.isTransientHousehold);
                Assert.IsFalse(household.isBoardingHouseGuestHousehold);
                Assert.AreEqual(string.Empty, household.lodgingBusinessInstanceId);
                Assert.IsFalse(household.isBoardingHouseLodging);
                Assert.AreEqual(string.Empty, household.boardingBusinessInstanceId);
                Assert.AreEqual(0, target.State.boardingCapacity);
                Assert.AreEqual(0, target.State.boardingUsed);
                Assert.AreEqual(0, target.State.boardingHouseCapacity);
                Assert.AreEqual(0, target.State.boardingHouseUsed);
                Assert.NotNull(target.State.rentalProperties);
                Assert.NotNull(target.State.rentalApplicants);
                Assert.AreEqual(0, target.State.rentalProperties.Count);
                Assert.AreEqual(0, target.State.rentalApplicants.Count);
                Assert.AreEqual(0, target.State.rentalVacancyCount);
                Assert.AreEqual(0, target.State.playerOwnedRentalVacancyCount);
                Assert.AreEqual(0, target.State.rentalApplicantCount);
                Assert.AreEqual(0, target.State.strongerHousingNeedCount);
                Assert.AreEqual(0f, target.State.rentalPressure01);
                Assert.AreEqual(1f, target.State.laborAbsorption01);
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void LegacyJsonPopulationLoadDefaultsOmittedSettlementFields()
        {
            GameObject targetObject = new("Settlement Legacy Json Target");
            try
            {
                string json = "{"
                    + "\"people\":[{"
                    + "\"id\":301,"
                    + "\"firstName\":\"JsonLegacy\","
                    + "\"lastName\":\"Worker\","
                    + "\"age\":32,"
                    + $"\"ageBand\":{(int)AgeBand.Adult18Plus},"
                    + $"\"laborAccessLevel\":{(int)LaborAccessLevel.FullLaborMarket},"
                    + "\"householdId\":30,"
                    + "\"homeBuildingId\":7,"
                    + "\"workplaceBuildingId\":-1,"
                    + $"\"scheduleState\":{(int)PopulationScheduleState.AtHome},"
                    + "\"currentDestinationBuildingId\":7"
                    + "}],"
                    + "\"households\":[{"
                    + "\"id\":30,"
                    + "\"householdName\":\"Json Legacy Household\","
                    + "\"surname\":\"Worker\","
                    + "\"homeBuildingId\":7,"
                    + "\"memberIds\":[301]"
                    + "}]"
                    + "}";
                PopulationSaveDto legacyDto = JsonUtility.FromJson<PopulationSaveDto>(json);

                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(legacyDto);

                PersonState person = target.State.GetPerson(301);
                Assert.NotNull(person);
                Assert.AreEqual(NewcomerArrivalProfile.SettledResident, person.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.StableHousehold, person.settlementArrangement);
                Assert.IsTrue(person.settlementPressure.initialized);
                Assert.AreEqual("Stable household", person.settlementPressure.summary);

                HouseholdState household = target.State.GetHousehold(30);
                Assert.NotNull(household);
                Assert.AreEqual(NewcomerArrivalProfile.SettledResident, household.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.StableHousehold, household.settlementArrangement);
                Assert.IsTrue(household.settlementPressure.initialized);
                Assert.NotNull(household.boarderPersonIds);
                Assert.AreEqual(0, household.boarderPersonIds.Count);
                Assert.IsFalse(household.isBoardingHouseGuestHousehold);
                Assert.AreEqual(string.Empty, household.lodgingBusinessInstanceId);
                Assert.IsFalse(household.isBoardingHouseLodging);
                Assert.AreEqual(string.Empty, household.boardingBusinessInstanceId);

                Assert.GreaterOrEqual(target.State.boardingCapacity, 0);
                Assert.GreaterOrEqual(target.State.boardingUsed, 0);
                Assert.GreaterOrEqual(target.State.renterHouseholdCount, 0);
                Assert.GreaterOrEqual(target.State.kinPlacementCount, 0);
                Assert.GreaterOrEqual(target.State.transientPersonCount, 0);
                Assert.GreaterOrEqual(target.State.unstableHouseholdCount, 0);
                Assert.GreaterOrEqual(target.State.departedUnsettledCount, 0);
                Assert.AreEqual(0, target.State.boardingHouseCapacity);
                Assert.AreEqual(0, target.State.boardingHouseUsed);
                Assert.NotNull(target.State.rentalProperties);
                Assert.NotNull(target.State.rentalApplicants);
                Assert.AreEqual(0, target.State.rentalProperties.Count);
                Assert.AreEqual(0, target.State.rentalApplicants.Count);
                Assert.AreEqual(0, target.State.rentalVacancyCount);
                Assert.AreEqual(0, target.State.playerOwnedRentalVacancyCount);
                Assert.AreEqual(0, target.State.rentalApplicantCount);
                Assert.AreEqual(0, target.State.strongerHousingNeedCount);
                Assert.AreEqual(0f, target.State.rentalPressure01);
                Assert.AreEqual(1f, target.State.laborAbsorption01);
                Assert.IsFalse(string.IsNullOrWhiteSpace(target.LastSettlementSummary));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
            }
        }

        private static void PopulateSettlementState(PopulationManager manager)
        {
            manager.State.people.Add(CreatePerson(
                100,
                10,
                NewcomerArrivalProfile.SettledResident,
                SettlementArrangement.StableHousehold,
                -1));
            manager.State.people.Add(CreatePerson(
                101,
                10,
                NewcomerArrivalProfile.BoarderSeekingWorker,
                SettlementArrangement.Boarding,
                10));
            manager.State.people.Add(CreatePerson(
                102,
                10,
                NewcomerArrivalProfile.KinLinkedArrival,
                SettlementArrangement.Kin,
                10));
            manager.State.people.Add(CreatePerson(
                103,
                11,
                NewcomerArrivalProfile.DistressedRelocationHousehold,
                SettlementArrangement.Transient,
                -1));

            manager.State.households.Add(new HouseholdState
            {
                id = 10,
                householdName = "Boarding House",
                surname = "Board",
                homeBuildingId = 5,
                weeklyIncomeSnapshot = 44,
                spendingMoneyCents = 25000,
                memberIds = new List<int> { 100, 102 },
                arrivalProfile = NewcomerArrivalProfile.RenterReadyHousehold,
                settlementArrangement = SettlementArrangement.Boarding,
                settlementPressure = SettlementPressureState.Create(
                    SettlementArrangement.Boarding,
                    0.42f,
                    0.57f,
                    0.61f,
                    3,
                    0.74f,
                    "Saved boarding pressure"),
                settlementReserveStrengthCents = 12345,
                baseBoardingCapacity = 2,
                boardingCapacity = 2,
                boarderPersonIds = new List<int> { 101 },
                hostsBoarders = true,
                isRenterHousehold = true,
                hasKinAbsorptionPressure = true,
                isTransientHousehold = false,
                crowdingPressure01 = 0.25f,
                boarderOverloadPressure01 = 0.1f,
                kinAbsorptionPressure01 = 0.2f,
                lastSettlementSummary = "Saved household summary"
            });

            manager.State.households.Add(new HouseholdState
            {
                id = 11,
                householdName = "Transient Household",
                surname = "Drift",
                homeBuildingId = 6,
                memberIds = new List<int> { 103 },
                arrivalProfile = NewcomerArrivalProfile.DistressedRelocationHousehold,
                settlementArrangement = SettlementArrangement.Transient,
                isTransientHousehold = true,
                settlementPressure = SettlementPressureState.Create(
                    SettlementArrangement.Transient,
                    0.2f,
                    0.82f,
                    0.35f,
                    2,
                    0.9f,
                    "Saved transient pressure")
            });

            manager.State.departedUnsettledCount = 2;
        }

        private static PersonState CreatePerson(
            int id,
            int householdId,
            NewcomerArrivalProfile profile,
            SettlementArrangement arrangement,
            int hostHouseholdId)
        {
            return new PersonState
            {
                id = id,
                firstName = $"Settler{id}",
                lastName = "Test",
                age = 28,
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
                professionId = "settlement_worker",
                professionName = "Settlement Worker",
                wage = new WageSnapshot
                {
                    weeklyWage = 20,
                    stabilityPercent = 70,
                    currencyId = "dollars"
                },
                health = new PersonHealthState(),
                homeBuildingId = 5,
                workplaceBuildingId = -1,
                scheduleState = PopulationScheduleState.AtHome,
                currentDestinationBuildingId = 5,
                arrivalProfile = profile,
                settlementArrangement = arrangement,
                settlementPressure = SettlementPressureState.Create(
                    arrangement,
                    0.48f,
                    0.44f,
                    0.62f,
                    4,
                    0.68f,
                    $"Saved {arrangement} pressure"),
                startingCashCents = 13500,
                laborUrgency01 = 0.82f,
                laborReadinessModifier = -6,
                preferredProfessionBias = "skilled labor",
                settlementDifficulty = 5,
                hostHouseholdId = hostHouseholdId
            };
        }

        private static PersonSaveDto FindPersonDto(PopulationSaveDto dto, int personId)
        {
            for (int i = 0; i < dto.people.Count; i++)
            {
                if (dto.people[i].id == personId)
                {
                    return dto.people[i];
                }
            }

            Assert.Fail($"Expected person dto {personId}.");
            return null;
        }

        private static HouseholdSaveDto FindHouseholdDto(PopulationSaveDto dto, int householdId)
        {
            for (int i = 0; i < dto.households.Count; i++)
            {
                if (dto.households[i].id == householdId)
                {
                    return dto.households[i];
                }
            }

            Assert.Fail($"Expected household dto {householdId}.");
            return null;
        }

        private static void AssertPersonSettlementMatches(PersonSaveDto expected, PersonState actual)
        {
            Assert.NotNull(actual);
            Assert.AreEqual(expected.arrivalProfile, actual.arrivalProfile);
            Assert.AreEqual(expected.settlementArrangement, actual.settlementArrangement);
            AssertPressureMatches(expected.settlementPressure, actual.settlementPressure);
            Assert.AreEqual(expected.startingCashCents, actual.startingCashCents);
            Assert.That(actual.laborUrgency01, Is.EqualTo(expected.laborUrgency01).Within(0.0001f));
            Assert.AreEqual(expected.laborReadinessModifier, actual.laborReadinessModifier);
            Assert.AreEqual(expected.preferredProfessionBias, actual.preferredProfessionBias);
            Assert.AreEqual(expected.settlementDifficulty, actual.settlementDifficulty);
            Assert.AreEqual(expected.hostHouseholdId, actual.hostHouseholdId);
        }

        private static void AssertHouseholdSettlementMatches(HouseholdSaveDto expected, HouseholdState actual)
        {
            Assert.NotNull(actual);
            Assert.AreEqual(expected.arrivalProfile, actual.arrivalProfile);
            Assert.AreEqual(expected.settlementArrangement, actual.settlementArrangement);
            AssertPressureMatches(expected.settlementPressure, actual.settlementPressure);
            Assert.AreEqual(expected.settlementReserveStrengthCents, actual.settlementReserveStrengthCents);
            Assert.AreEqual(expected.baseBoardingCapacity, actual.baseBoardingCapacity);
            Assert.AreEqual(expected.boardingCapacity, actual.boardingCapacity);
            CollectionAssert.AreEqual(expected.boarderPersonIds, actual.boarderPersonIds);
            Assert.AreEqual(expected.hostsBoarders, actual.hostsBoarders);
            Assert.AreEqual(expected.isRenterHousehold, actual.isRenterHousehold);
            Assert.AreEqual(expected.hasKinAbsorptionPressure, actual.hasKinAbsorptionPressure);
            Assert.AreEqual(expected.isTransientHousehold, actual.isTransientHousehold);
            Assert.AreEqual(expected.isBoardingHouseGuestHousehold, actual.isBoardingHouseGuestHousehold);
            Assert.AreEqual(expected.lodgingBusinessInstanceId, actual.lodgingBusinessInstanceId);
            Assert.AreEqual(expected.isBoardingHouseLodging, actual.isBoardingHouseLodging);
            Assert.AreEqual(expected.boardingBusinessInstanceId, actual.boardingBusinessInstanceId);
            Assert.That(actual.crowdingPressure01, Is.EqualTo(expected.crowdingPressure01).Within(0.0001f));
            Assert.That(actual.boarderOverloadPressure01, Is.EqualTo(expected.boarderOverloadPressure01).Within(0.0001f));
            Assert.That(actual.kinAbsorptionPressure01, Is.EqualTo(expected.kinAbsorptionPressure01).Within(0.0001f));
            Assert.AreEqual(expected.lastSettlementSummary, actual.lastSettlementSummary);
        }

        private static void AssertPressureMatches(SettlementPressureState expected, SettlementPressureState actual)
        {
            Assert.AreEqual(expected.initialized, actual.initialized);
            Assert.That(actual.stability01, Is.EqualTo(expected.stability01).Within(0.0001f));
            Assert.That(actual.housingFriction01, Is.EqualTo(expected.housingFriction01).Within(0.0001f));
            Assert.That(actual.laborReadiness01, Is.EqualTo(expected.laborReadiness01).Within(0.0001f));
            Assert.AreEqual(expected.weeksInArrangement, actual.weeksInArrangement);
            Assert.That(actual.homeAmbition01, Is.EqualTo(expected.homeAmbition01).Within(0.0001f));
            Assert.AreEqual(expected.summary, actual.summary);
        }

        private static void AddBuildingToTownWorld(
            TownWorldController townWorld,
            BuildingDefinition definition,
            int buildingId,
            bool playerOwned)
        {
            FieldInfo buildingsField = typeof(TownWorldController).GetField("buildings", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(buildingsField);
            List<PlacedBuilding> buildings = buildingsField.GetValue(townWorld) as List<PlacedBuilding>;
            Assert.NotNull(buildings);
            buildings.Add(new PlacedBuilding
            {
                id = buildingId,
                definition = definition,
                playerOwned = playerOwned
            });
        }

        private static void SetPrivateField<T>(T target, string fieldName, object value)
        {
            FieldInfo field = typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }
    }
}
