using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    public sealed class NewcomerSettlementPlannerTests
    {
        [Test]
        public void NewcomerSettlementRuntimeTypesAreAvailableToEditorTests()
        {
            NewcomerSettlementHomeOption option = new(42, 1, true, true, 2);
            SettlementPressureState pressure = SettlementPressureState.Create(
                SettlementArrangement.Boarding,
                0.5f,
                0.4f,
                0.6f,
                2,
                0.7f,
                "Compile path boarding pressure");
            NewcomerProfileDefinition profile = NewcomerSettlementPlanner.GetProfileDefinition(
                NewcomerArrivalProfile.BoarderSeekingWorker);

            Assert.AreEqual("LandLedgers.Population.NewcomerSettlementPlanner", typeof(NewcomerSettlementPlanner).FullName);
            Assert.AreEqual(42, option.BuildingId);
            Assert.AreEqual(2, option.BaseBoardingCapacity);
            Assert.IsTrue(pressure.initialized);
            Assert.AreEqual(NewcomerArrivalProfile.BoarderSeekingWorker, profile.Profile);
            Assert.AreEqual("boarding", NewcomerSettlementEvaluator.GetArrangementDisplayName(SettlementArrangement.Boarding));
        }

        [Test]
        public void ProfileDefinitionsExposeGroundedArrivalVariation()
        {
            NewcomerProfileDefinition laborer = NewcomerSettlementPlanner.GetProfileDefinition(NewcomerArrivalProfile.LoneLaborer);
            NewcomerProfileDefinition boarder = NewcomerSettlementPlanner.GetProfileDefinition(NewcomerArrivalProfile.BoarderSeekingWorker);
            NewcomerProfileDefinition renter = NewcomerSettlementPlanner.GetProfileDefinition(NewcomerArrivalProfile.RenterReadyHousehold);
            NewcomerProfileDefinition skilled = NewcomerSettlementPlanner.GetProfileDefinition(NewcomerArrivalProfile.SkilledCapitalizedArrival);
            NewcomerProfileDefinition distressed = NewcomerSettlementPlanner.GetProfileDefinition(NewcomerArrivalProfile.DistressedRelocationHousehold);
            NewcomerProfileDefinition elder = NewcomerSettlementPlanner.GetProfileDefinition(NewcomerArrivalProfile.WidowElderDependentRelocation);

            Assert.Greater(boarder.BoardingPreference01, renter.BoardingPreference01);
            Assert.Greater(renter.RentingPreference01, laborer.RentingPreference01);
            Assert.Greater(skilled.DirectSettlementPreference01, renter.DirectSettlementPreference01);
            Assert.Greater(skilled.MinCashCents, distressed.MaxCashCents);
            Assert.Greater(distressed.MaxHouseholdSize, laborer.MaxHouseholdSize);
            Assert.Greater(elder.KinPreference01, skilled.KinPreference01);
            StringAssert.Contains("labor", boarder.ProfessionBias);
            StringAssert.Contains("skilled", skilled.ProfessionBias);
        }

        [Test]
        public void BoarderSeekingWorkersFillBoardingCapacityBeforeHomeSlots()
        {
            PopulationState state = CreateStateWithHost(1, 1, 2);
            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.BoarderSeekingWorker, 2);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(1, 1, false, true, 2)
            };

            int created = NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(4), homes);

            HouseholdState host = state.GetHousehold(1);
            Assert.AreEqual(2, created);
            Assert.AreEqual(2, state.boardingUsed);
            Assert.AreEqual(2, state.boardingCapacity);
            Assert.AreEqual(2, host.boarderPersonIds.Count);
            Assert.AreEqual(1, state.households.Count, "Boarders should use the host household rather than consume vacant home slots.");
            Assert.AreEqual(2, CountPeopleInArrangement(state, SettlementArrangement.Boarding));
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void BoardingHouseBusinessCapacityAcceptsNewcomersBeforeHouseholdRooms()
        {
            PopulationState state = CreateStateWithHost(1, 1, 1);
            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.BoarderSeekingWorker, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(1, 1, false, true, 1)
            };
            List<NewcomerSettlementBoardingOption> boardingHouses = new()
            {
                new("boarding-1", 20, "Porter Boarding House", 2, true, 10000, 1, 2)
            };

            int created = NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(11), homes, boardingHouses);

            HouseholdState lodging = FindBoardingHouseLodging(state, "boarding-1");
            HouseholdState host = state.GetHousehold(1);
            Assert.AreEqual(1, created);
            Assert.NotNull(lodging);
            Assert.IsTrue(lodging.isBoardingHouseLodging);
            Assert.AreEqual(1, lodging.boarderPersonIds.Count);
            Assert.AreEqual(0, host.boarderPersonIds.Count);
            Assert.AreEqual(2, state.boardingHouseCapacity);
            Assert.AreEqual(1, state.boardingHouseUsed);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void KinLinkedArrivalsJoinExistingHouseholdWhenAvailable()
        {
            PopulationState state = CreateStateWithHost(1, 1, 0);
            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.KinLinkedArrival, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(1, 1, false, true, 0)
            };

            int initialPeople = state.people.Count;
            int created = NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(8), homes);

            HouseholdState host = state.GetHousehold(1);
            Assert.GreaterOrEqual(created, 1);
            Assert.Greater(state.people.Count, initialPeople);
            Assert.IsTrue(host.hasKinAbsorptionPressure);
            Assert.Greater(host.kinAbsorptionPressure01, 0f);
            Assert.GreaterOrEqual(CountPeopleInArrangement(state, SettlementArrangement.Kin), 1);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void RenterReadyAndCapitalizedArrivalsPreferDirectAccommodation()
        {
            AssertDirectArrivalUsesVacantHome(NewcomerArrivalProfile.RenterReadyHousehold, SettlementArrangement.Renting);
            AssertDirectArrivalUsesVacantHome(NewcomerArrivalProfile.SkilledCapitalizedArrival, SettlementArrangement.StableHousehold);
        }

        [Test]
        public void HousingShortageCreatesTransientPressure()
        {
            PopulationState state = new();
            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.DistressedRelocationHousehold, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(1, 0, false, false, 0)
            };

            int created = NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(15), homes);

            Assert.Greater(created, 0);
            Assert.Greater(state.transientPersonCount, 0);
            Assert.Greater(state.unstableHouseholdCount, 0);
            Assert.Greater(state.housingPressure01, 0f);
            Assert.Less(state.laborAbsorption01, 1f);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void BoarderRoomIncreasesBoardingCapacityAndReducesPressure()
        {
            PopulationState state = CreateStateWithHost(1, 1, 0);
            NewcomerSettlementPlanner.RefreshSettlementMetrics(state);
            Assert.AreEqual(0, state.boardingCapacity);

            HouseholdState household = state.GetHousehold(1);
            household.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.BoarderRoom, 2));
            NewcomerSettlementPlanner.RefreshSettlementMetrics(state);

            Assert.AreEqual(1, household.boardingCapacity);
            Assert.AreEqual(1, state.boardingCapacity);
            Assert.AreEqual(0, state.boardingUsed);
            Assert.AreEqual(0f, household.boarderOverloadPressure01);
        }

        [Test]
        public void BoardersAccumulateSavingsAndMoveIntoVacantRentalsWhenEligible()
        {
            PopulationState state = CreateBoardingHouseBoarderState(out PersonState boarder);
            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.BoarderSeekingWorker, 0);
            settings.boarderStableMoveMinWeeks = 1;
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(30, 1, false, true, 0, true, true, "Player Cottage")
            };
            List<NewcomerSettlementBoardingOption> boardingHouses = new()
            {
                new("boarding-1", 20, "Porter Boarding House", 1, false, 10000, 1, 1)
            };

            int moved = NewcomerSettlementPlanner.ResolveWeeklySettlementProgression(state, settings, homes, new System.Random(19), boardingHouses);

            Assert.AreEqual(1, moved);
            Assert.AreEqual(SettlementArrangement.Renting, boarder.settlementArrangement);
            Assert.AreEqual(30, boarder.homeBuildingId);
            Assert.AreEqual(0, state.boardingHouseUsed);
            Assert.AreEqual(1, state.renterHouseholdCount);
            Assert.AreEqual(1, state.rentalApplicants.Count);
            Assert.IsTrue(state.rentalApplicants[0].acceptedLastWeek);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void ApplicantPressureAppearsWhenEligibleBoardersHaveNoVacancy()
        {
            PopulationState state = CreateBoardingHouseBoarderState(out _);
            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.BoarderSeekingWorker, 0);
            settings.boarderStableMoveMinWeeks = 1;
            List<NewcomerSettlementHomeOption> homes = new();
            List<NewcomerSettlementBoardingOption> boardingHouses = new()
            {
                new("boarding-1", 20, "Porter Boarding House", 1, false, 10000, 1, 1)
            };

            int moved = NewcomerSettlementPlanner.ResolveWeeklySettlementProgression(state, settings, homes, new System.Random(23), boardingHouses);

            Assert.AreEqual(0, moved);
            Assert.GreaterOrEqual(state.rentalApplicantCount, 1);
            Assert.Greater(state.rentalPressure01, 0f);
            StringAssert.Contains("applicants", state.lastRentalSummary);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void KinHouseholdWithSavingsMovesIntoVacantRentalAsWholeHousehold()
        {
            PopulationState state = CreateKinHouseholdApplicantState();
            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.KinLinkedArrival, 0);
            settings.boarderStableMoveMinWeeks = 2;
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(31, 1, false, true, 0, true, true, "Player Duplex"),
                new(32, 1, false, true, 0, false, true, "Freight Row Rooms")
            };

            int moved = NewcomerSettlementPlanner.ResolveWeeklySettlementProgression(state, settings, homes, new System.Random(29));

            HouseholdState movedHousehold = state.GetHousehold(12);
            Assert.AreEqual(2, moved);
            Assert.NotNull(movedHousehold);
            Assert.AreEqual(SettlementArrangement.Renting, movedHousehold.settlementArrangement);
            Assert.AreEqual(31, movedHousehold.homeBuildingId);
            Assert.IsTrue(movedHousehold.isRenterHousehold);
            Assert.AreEqual(2, movedHousehold.memberIds.Count);
            Assert.AreEqual(0, CountPeopleInArrangement(state, SettlementArrangement.Kin));
            Assert.AreEqual(1, state.renterHouseholdCount);
            Assert.AreEqual(1, state.rentalApplicantCount);
            Assert.IsTrue(state.rentalApplicants[0].acceptedLastWeek);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void HouseholdHousingPressureReducesLaborReadinessForBoarders()
        {
            PopulationState state = CreateOverloadedBoardingState(out PersonState boarder);
            NewcomerSettlementPlanner.RefreshSettlementMetrics(state);

            float readiness = NewcomerSettlementEvaluator.GetLaborReadiness01(boarder);

            Assert.Less(readiness, 0.6f);
            Assert.Greater(boarder.settlementPressure.housingFriction01, 0.5f);
            Assert.Greater(state.GetHousehold(boarder.householdId).boarderOverloadPressure01, 0f);
        }

        [Test]
        public void BoardingHouseProfileLoadsAsSharedBusinessService()
        {
            BusinessProfileDefinition profile = Resources.Load<BusinessProfileDefinition>("Core/Economy/BusinessProfiles/BoardingHouseProfile");

            Assert.NotNull(profile);
            Assert.AreEqual(BusinessType.BoardingHouse, profile.Business.BusinessType);
            Assert.AreEqual(BusinessThroughputMode.Service, profile.ThroughputMode);
            Assert.Greater(profile.BaselineDailyServiceCapacity, 0);
            Assert.Greater(profile.Business.WorkerSlots.Length, 0);
        }

        [Test]
        public void StableHousingImprovesLaborReadinessAndCandidateOrdering()
        {
            PersonState stable = CreatePerson(1, 1, 1, SettlementArrangement.StableHousehold);
            PersonState transient = CreatePerson(2, 2, 1, SettlementArrangement.Transient);
            WorkerSlotState slot = new("clerk_helper", "Clerk / Helper", 1200, true);

            Assert.Greater(
                NewcomerSettlementEvaluator.GetLaborReadiness01(stable),
                NewcomerSettlementEvaluator.GetLaborReadiness01(transient));
            Assert.Less(
                WorkerRoleFitEvaluator.CompareCandidates(stable, transient, BusinessType.GeneralStore, slot),
                0);
        }

        [Test]
        public void SaveLoadRoundTripsSettlementStateAndLegacyDtosDefaultStable()
        {
            GameObject sourceObject = new("Settlement Save Source");
            GameObject targetObject = new("Settlement Save Target");
            GameObject legacyObject = new("Settlement Legacy Target");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                PersonState boarder = CreatePerson(5, 7, 3, SettlementArrangement.Boarding);
                boarder.arrivalProfile = NewcomerArrivalProfile.BoarderSeekingWorker;
                boarder.hostHouseholdId = 7;
                boarder.startingCashCents = 1800;
                boarder.laborUrgency01 = 0.9f;
                boarder.laborReadinessModifier = -6;
                boarder.preferredProfessionBias = "labor";
                boarder.settlementDifficulty = 5;
                boarder.settlementPressure = SettlementPressureState.Create(SettlementArrangement.Boarding, 0.52f, 0.35f, 0.72f, 3, 0.68f, "Boarding for test");

                HouseholdState host = CreateHousehold(7, 3, 1, boarder.id);
                host.boarderPersonIds.Add(boarder.id);
                host.hostsBoarders = true;
                host.baseBoardingCapacity = 1;
                host.boardingCapacity = 1;
                host.lastSettlementSummary = "Boarding host for test";
                source.State.people.Add(boarder);
                source.State.households.Add(host);

                PopulationSaveDto dto = source.CaptureSaveDto();
                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                PersonState restored = target.State.GetPerson(boarder.id);
                HouseholdState restoredHost = target.State.GetHousehold(host.id);
                Assert.AreEqual(NewcomerArrivalProfile.BoarderSeekingWorker, restored.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.Boarding, restored.settlementArrangement);
                Assert.AreEqual(3, restored.settlementPressure.weeksInArrangement);
                Assert.AreEqual(1800, restored.startingCashCents);
                Assert.AreEqual("labor", restored.preferredProfessionBias);
                Assert.AreEqual(1, restoredHost.boarderPersonIds.Count);
                Assert.AreEqual(1, target.BoardingUsed);
                Assert.AreEqual(1, target.BoardingCapacity);

                PopulationSaveDto legacyDto = new();
                legacyDto.people.Add(new PersonSaveDto
                {
                    id = 9,
                    firstName = "Legacy",
                    lastName = "Resident",
                    age = 30,
                    ageBand = AgeBand.Adult18Plus,
                    laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                    householdId = 10,
                    homeBuildingId = 4,
                    wage = WageSnapshot.None()
                });
                legacyDto.households.Add(new HouseholdSaveDto
                {
                    id = 10,
                    householdName = "Legacy Household",
                    surname = "Resident",
                    homeBuildingId = 4,
                    memberIds = new List<int> { 9 }
                });

                PopulationManager legacy = legacyObject.AddComponent<PopulationManager>();
                legacy.LoadFromSaveDto(legacyDto);

                PersonState legacyPerson = legacy.State.GetPerson(9);
                HouseholdState legacyHousehold = legacy.State.GetHousehold(10);
                Assert.AreEqual(NewcomerArrivalProfile.SettledResident, legacyPerson.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.StableHousehold, legacyPerson.settlementArrangement);
                Assert.AreEqual(SettlementArrangement.StableHousehold, legacyHousehold.settlementArrangement);
                Assert.AreEqual(0, legacy.BoardingUsed);
                Assert.AreEqual(0, legacy.TransientPersonCount);
                Assert.AreEqual(0f, legacy.HousingPressure01);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(legacyObject);
            }
        }

        private static void AssertDirectArrivalUsesVacantHome(NewcomerArrivalProfile profile, SettlementArrangement expectedArrangement)
        {
            PopulationState state = CreateStateWithHost(1, 1, 0);
            PopulationGenerationSettings settings = CreateSettings(profile, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(1, 1, false, true, 0),
                new(2, 1, false, true, 0)
            };

            int created = NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random((int)profile * 13), homes);

            Assert.GreaterOrEqual(created, 1);
            Assert.GreaterOrEqual(CountPeopleInArrangement(state, expectedArrangement), 1);
            Assert.NotNull(FindHouseholdByHome(state, 2));
            Object.DestroyImmediate(settings);
        }

        private static PopulationState CreateStateWithHost(int householdId, int homeBuildingId, int baseBoardingCapacity)
        {
            PopulationState state = new();
            PersonState resident = CreatePerson(100 + householdId, householdId, homeBuildingId, SettlementArrangement.StableHousehold);
            HouseholdState household = CreateHousehold(householdId, homeBuildingId, baseBoardingCapacity, resident.id);
            state.people.Add(resident);
            state.households.Add(household);
            return state;
        }

        private static PersonState CreatePerson(int id, int householdId, int homeBuildingId, SettlementArrangement arrangement)
        {
            return new PersonState
            {
                id = id,
                firstName = $"Test{id}",
                lastName = "Resident",
                age = 28,
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
                professionId = string.Empty,
                professionName = "No assigned job",
                wage = WageSnapshot.None(),
                hiddenWorkerTraits = WorkerHiddenTraits.Create(70, 70, 70, 70, 70, 70),
                visibleWorkerProfile = WorkerVisibleProfile.Create(70, 1200, "Test worker"),
                workerTraitVisibility = WorkerTraitVisibility.FirstPassDefault(),
                apprenticeship = WorkerApprenticeshipState.FirstPassDefault(),
                health = new PersonHealthState(),
                arrivalProfile = arrangement == SettlementArrangement.StableHousehold
                    ? NewcomerArrivalProfile.SettledResident
                    : NewcomerArrivalProfile.LoneLaborer,
                settlementArrangement = arrangement,
                settlementPressure = SettlementPressureState.Create(
                    arrangement,
                    NewcomerSettlementEvaluator.GetDefaultSettlementStability(arrangement),
                    NewcomerSettlementEvaluator.GetDefaultHousingFriction(arrangement),
                    NewcomerSettlementEvaluator.GetDefaultLaborReadiness(arrangement),
                    0,
                    NewcomerSettlementEvaluator.GetDefaultHomeAmbition(arrangement),
                    arrangement.ToString()),
                homeBuildingId = homeBuildingId,
                workplaceBuildingId = -1,
                scheduleState = PopulationScheduleState.AtHome,
                currentDestinationBuildingId = homeBuildingId
            };
        }

        private static HouseholdState CreateHousehold(int householdId, int homeBuildingId, int baseBoardingCapacity, int memberId)
        {
            HouseholdState household = new()
            {
                id = householdId,
                householdName = $"Household {householdId}",
                surname = "Resident",
                homeBuildingId = homeBuildingId,
                memberIds = new List<int> { memberId },
                spendingMoneyCents = 10000,
                weeklyIncomeSnapshot = 20,
                baseBoardingCapacity = baseBoardingCapacity,
                boardingCapacity = baseBoardingCapacity,
                settlementArrangement = SettlementArrangement.StableHousehold,
                settlementPressure = SettlementPressureState.Create(
                    SettlementArrangement.StableHousehold,
                    0.92f,
                    0.05f,
                    0.92f,
                    0,
                    0.20f,
                    "Stable household")
            };
            household.EnsureHouseholdReservesInitialized(true);
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(household);
            return household;
        }

        private static PopulationGenerationSettings CreateSettings(NewcomerArrivalProfile onlyProfile, int minimumArrivalHouseholds)
        {
            PopulationGenerationSettings settings = ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            settings.newcomerArrivalRate = 0f;
            settings.minimumNewcomerArrivalHouseholds = minimumArrivalHouseholds;
            settings.residentialBoardingBaseCapacity = 0;
            settings.mixedUseBoardingBaseCapacity = 0;
            settings.pressuredArrivalDepartureChance = 0f;
            settings.loneLaborerArrivalWeight = 0;
            settings.kinLinkedArrivalWeight = 0;
            settings.boarderSeekingWorkerArrivalWeight = 0;
            settings.renterReadyHouseholdArrivalWeight = 0;
            settings.skilledCapitalizedArrivalWeight = 0;
            settings.distressedRelocationArrivalWeight = 0;
            settings.widowElderRelocationArrivalWeight = 0;

            switch (onlyProfile)
            {
                case NewcomerArrivalProfile.KinLinkedArrival:
                    settings.kinLinkedArrivalWeight = 1;
                    break;
                case NewcomerArrivalProfile.BoarderSeekingWorker:
                    settings.boarderSeekingWorkerArrivalWeight = 1;
                    break;
                case NewcomerArrivalProfile.RenterReadyHousehold:
                    settings.renterReadyHouseholdArrivalWeight = 1;
                    break;
                case NewcomerArrivalProfile.SkilledCapitalizedArrival:
                    settings.skilledCapitalizedArrivalWeight = 1;
                    break;
                case NewcomerArrivalProfile.DistressedRelocationHousehold:
                    settings.distressedRelocationArrivalWeight = 1;
                    break;
                case NewcomerArrivalProfile.WidowElderDependentRelocation:
                    settings.widowElderRelocationArrivalWeight = 1;
                    break;
                default:
                    settings.loneLaborerArrivalWeight = 1;
                    break;
            }

            return settings;
        }

        private static int CountPeopleInArrangement(PopulationState state, SettlementArrangement arrangement)
        {
            int count = 0;
            for (int i = 0; i < state.people.Count; i++)
            {
                if (state.people[i] != null && state.people[i].settlementArrangement == arrangement)
                {
                    count++;
                }
            }

            return count;
        }

        private static HouseholdState FindHouseholdByHome(PopulationState state, int homeBuildingId)
        {
            for (int i = 0; i < state.households.Count; i++)
            {
                if (state.households[i] != null && state.households[i].homeBuildingId == homeBuildingId)
                {
                    return state.households[i];
                }
            }

            return null;
        }

        private static HouseholdState FindBoardingHouseLodging(PopulationState state, string businessInstanceId)
        {
            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household != null
                    && household.isBoardingHouseLodging
                    && household.boardingBusinessInstanceId == businessInstanceId)
                {
                    return household;
                }
            }

            return null;
        }

        private static PopulationState CreateBoardingHouseBoarderState(out PersonState boarder)
        {
            PopulationState state = new();
            boarder = CreatePerson(40, 4, 20, SettlementArrangement.Boarding);
            boarder.arrivalProfile = NewcomerArrivalProfile.BoarderSeekingWorker;
            boarder.startingCashCents = 3600;
            boarder.wage = new WageSnapshot
            {
                weeklyWage = 18,
                stabilityPercent = 82,
                currencyId = "dollars"
            };
            boarder.settlementPressure = SettlementPressureState.Create(
                SettlementArrangement.Boarding,
                0.68f,
                0.32f,
                0.78f,
                1,
                0.72f,
                "Ready for rented rooms");

            HouseholdState lodging = new()
            {
                id = 4,
                householdName = "Porter Boarding House Guests",
                surname = "Boarding",
                homeBuildingId = 20,
                memberIds = new List<int> { boarder.id },
                boarderPersonIds = new List<int> { boarder.id },
                baseBoardingCapacity = 1,
                boardingCapacity = 1,
                settlementArrangement = SettlementArrangement.Boarding,
                arrivalProfile = NewcomerArrivalProfile.BoarderSeekingWorker,
                isBoardingHouseLodging = true,
                boardingBusinessInstanceId = "boarding-1",
                demandSnapshot = HouseholdDemandSnapshot.Empty(),
                lastSettlementSummary = "Boarding House lodging"
            };
            lodging.EnsureHouseholdReservesInitialized(false);
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(lodging);

            state.people.Add(boarder);
            state.households.Add(lodging);
            return state;
        }

        private static PopulationState CreateKinHouseholdApplicantState()
        {
            PopulationState state = new();
            HouseholdState host = CreateHousehold(1, 10, 0, 101);
            PersonState resident = CreatePerson(101, 1, 10, SettlementArrangement.StableHousehold);
            state.people.Add(resident);
            state.households.Add(host);

            HouseholdState kinHousehold = new()
            {
                id = 12,
                householdName = "Mercer Kin",
                surname = "Mercer",
                homeBuildingId = 10,
                memberIds = new List<int> { 1201, 1202 },
                spendingMoneyCents = 6800,
                weeklyIncomeSnapshot = 34,
                settlementReserveStrengthCents = 5600,
                arrivalProfile = NewcomerArrivalProfile.KinLinkedArrival,
                settlementArrangement = SettlementArrangement.Kin,
                dwellingKind = HouseholdDwellingKind.KinSharedHousehold,
                hasKinAbsorptionPressure = true,
                demandSnapshot = HouseholdDemandSnapshot.Empty()
            };
            kinHousehold.settlementPressure = SettlementPressureState.Create(
                SettlementArrangement.Kin,
                0.62f,
                0.34f,
                0.72f,
                3,
                0.66f,
                "Saving toward rented rooms");
            kinHousehold.EnsureHouseholdReservesInitialized(false);
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(kinHousehold);

            PersonState adult = CreatePerson(1201, 12, 10, SettlementArrangement.Kin);
            adult.arrivalProfile = NewcomerArrivalProfile.KinLinkedArrival;
            adult.startingCashCents = 4200;
            adult.wage = new WageSnapshot { weeklyWage = 20, stabilityPercent = 76, currencyId = "dollars" };
            adult.settlementPressure = SettlementPressureState.Create(SettlementArrangement.Kin, 0.62f, 0.34f, 0.74f, 3, 0.64f, "Ready to rent");

            PersonState dependent = CreatePerson(1202, 12, 10, SettlementArrangement.Kin);
            dependent.arrivalProfile = NewcomerArrivalProfile.KinLinkedArrival;
            dependent.laborAccessLevel = LaborAccessLevel.None;
            dependent.wage = WageSnapshot.None();
            dependent.startingCashCents = 1400;
            dependent.settlementPressure = SettlementPressureState.Create(SettlementArrangement.Kin, 0.58f, 0.36f, 0.52f, 3, 0.61f, "Waiting on rooms");

            state.people.Add(adult);
            state.people.Add(dependent);
            state.households.Add(kinHousehold);
            return state;
        }

        private static PopulationState CreateOverloadedBoardingState(out PersonState boarder)
        {
            PopulationState state = new();
            HouseholdState host = new()
            {
                id = 21,
                householdName = "Crowded Rooms",
                surname = "Porter",
                homeBuildingId = 88,
                memberIds = new List<int> { 2101, 2102 },
                boarderPersonIds = new List<int> { 2101, 2102 },
                baseBoardingCapacity = 1,
                boardingCapacity = 1,
                hostsBoarders = true,
                settlementArrangement = SettlementArrangement.Boarding,
                arrivalProfile = NewcomerArrivalProfile.BoarderSeekingWorker,
                isBoardingHouseLodging = true,
                isBoardingHouseGuestHousehold = true,
                boardingBusinessInstanceId = "crowded-house",
                lodgingBusinessInstanceId = "crowded-house",
                demandSnapshot = HouseholdDemandSnapshot.Empty()
            };

            PersonState first = CreatePerson(2101, 21, 88, SettlementArrangement.Boarding);
            first.arrivalProfile = NewcomerArrivalProfile.BoarderSeekingWorker;
            first.startingCashCents = 2200;
            first.wage = new WageSnapshot { weeklyWage = 16, stabilityPercent = 80, currencyId = "dollars" };

            boarder = CreatePerson(2102, 21, 88, SettlementArrangement.Boarding);
            boarder.arrivalProfile = NewcomerArrivalProfile.BoarderSeekingWorker;
            boarder.startingCashCents = 2400;
            boarder.wage = new WageSnapshot { weeklyWage = 14, stabilityPercent = 78, currencyId = "dollars" };

            state.people.Add(first);
            state.people.Add(boarder);
            state.households.Add(host);
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(host);
            return state;
        }
    }
}
