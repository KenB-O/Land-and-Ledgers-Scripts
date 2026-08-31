using System;
using System.Collections.Generic;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Population
{
    public static class NewcomerSettlementPlanner
    {
        private static readonly NewcomerProfileDefinition LoneLaborer = new(
            NewcomerArrivalProfile.LoneLaborer, 1200, 5200, 1, 1, 0.78f, 0.18f, 0.05f, 0.08f, 0.86f, "labor", 4);

        private static readonly NewcomerProfileDefinition KinLinked = new(
            NewcomerArrivalProfile.KinLinkedArrival, 800, 3800, 1, 2, 0.18f, 0.15f, 0.82f, 0.08f, 0.62f, "domestic", 3);

        private static readonly NewcomerProfileDefinition BoarderSeekingWorker = new(
            NewcomerArrivalProfile.BoarderSeekingWorker, 900, 4500, 1, 1, 0.88f, 0.10f, 0.04f, 0.04f, 0.92f, "labor", 5);

        private static readonly NewcomerProfileDefinition RenterReady = new(
            NewcomerArrivalProfile.RenterReadyHousehold, 6000, 16000, 2, 4, 0.12f, 0.78f, 0.08f, 0.30f, 0.58f, "commercial", 2);

        private static readonly NewcomerProfileDefinition SkilledCapitalized = new(
            NewcomerArrivalProfile.SkilledCapitalizedArrival, 12000, 30000, 1, 3, 0.10f, 0.34f, 0.06f, 0.72f, 0.50f, "skilled trade cash", 1);

        private static readonly NewcomerProfileDefinition DistressedRelocation = new(
            NewcomerArrivalProfile.DistressedRelocationHousehold, 250, 3000, 2, 5, 0.22f, 0.22f, 0.25f, 0.02f, 0.76f, "labor domestic", 7);

        private static readonly NewcomerProfileDefinition WidowElderRelocation = new(
            NewcomerArrivalProfile.WidowElderDependentRelocation, 700, 6000, 1, 2, 0.34f, 0.18f, 0.52f, 0.04f, 0.34f, "domestic dependent", 5);

        public static List<NewcomerSettlementHomeOption> BuildHomeOptions(TownWorldController townWorld, PopulationGenerationSettings settings)
        {
            settings ??= ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            settings.Sanitize();

            List<NewcomerSettlementHomeOption> result = new();
            if (townWorld == null || townWorld.Buildings == null)
            {
                return result;
            }

            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding building = townWorld.Buildings[i];
                BuildingDefinition definition = building != null ? building.definition : null;
                if (definition == null || !definition.CanHostHouseholds)
                {
                    continue;
                }

                bool mixedUse = definition.IsMixedUse || definition.UsesUpperFloorResidential;
                int baseBoardingCapacity = mixedUse
                    ? settings.mixedUseBoardingBaseCapacity
                    : settings.residentialBoardingBaseCapacity;
                result.Add(new NewcomerSettlementHomeOption(
                    building.id,
                    definition.ResidentHouseholdCapacity,
                    mixedUse,
                    baseBoardingCapacity > 0,
                    baseBoardingCapacity,
                    building.playerOwned,
                    definition.CanHostHouseholds,
                    definition.DisplayName));
            }

            return result;
        }

        public static int GetBaseBoardingCapacity(PopulationGenerationSettings settings, bool mixedUse, bool canHostBoarding)
        {
            if (!canHostBoarding)
            {
                return 0;
            }

            settings ??= ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            settings.Sanitize();
            return mixedUse ? settings.mixedUseBoardingBaseCapacity : settings.residentialBoardingBaseCapacity;
        }

        public static int GenerateNewcomerArrivals(
            PopulationState state,
            PopulationGenerationSettings settings,
            System.Random random,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions,
            IReadOnlyList<NewcomerSettlementBoardingOption> boardingHouseOptions = null)
        {
            if (state == null)
            {
                return 0;
            }

            settings ??= ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            settings.Sanitize();
            random ??= new System.Random(0);
            homeOptions ??= Array.Empty<NewcomerSettlementHomeOption>();
            boardingHouseOptions ??= Array.Empty<NewcomerSettlementBoardingOption>();

            EnsureBoardingHouseHouseholds(state, boardingHouseOptions);
            InitializeExistingHouseholds(state, homeOptions);
            int homeCapacity = CountHomeCapacity(homeOptions);
            int requestedArrivals = Mathf.Max(
                homeCapacity > 0 ? Mathf.RoundToInt(homeCapacity * settings.newcomerArrivalRate) : 0,
                Mathf.Min(settings.minimumNewcomerArrivalHouseholds, Mathf.Max(1, homeCapacity + 1)));
            int maxArrivals = Mathf.Max(requestedArrivals, homeCapacity + Mathf.Max(2, homeCapacity / 2));
            requestedArrivals = Mathf.Clamp(requestedArrivals, 0, maxArrivals);

            int createdPeople = 0;
            for (int i = 0; i < requestedArrivals; i++)
            {
                NewcomerProfileDefinition profile = RollProfile(settings, random);
                createdPeople += PlaceArrival(state, settings, random, homeOptions, profile);
            }

            RefreshSettlementMetrics(state, homeOptions, boardingHouseOptions);
            return createdPeople;
        }

        public static int ResolveWeeklySettlementProgression(
            PopulationState state,
            PopulationGenerationSettings settings,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions,
            System.Random random = null,
            IReadOnlyList<NewcomerSettlementBoardingOption> boardingHouseOptions = null)
        {
            if (state == null || state.people == null || state.households == null)
            {
                return 0;
            }

            settings ??= ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            settings.Sanitize();
            random ??= new System.Random(0);
            homeOptions ??= Array.Empty<NewcomerSettlementHomeOption>();
            boardingHouseOptions ??= Array.Empty<NewcomerSettlementBoardingOption>();
            EnsureBoardingHouseHouseholds(state, boardingHouseOptions);
            ResetWeeklySettlementOutcome(state);

            int changes = 0;
            for (int householdIndex = 0; householdIndex < state.households.Count; householdIndex++)
            {
                HouseholdState household = state.households[householdIndex];
                if (household == null)
                {
                    continue;
                }

                household.EnsureSettlementStateInitialized();
                if (household.settlementArrangement != SettlementArrangement.Departed)
                {
                    household.settlementPressure.weeksInArrangement++;
                }
            }

            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                if (person == null)
                {
                    continue;
                }

                NewcomerSettlementEvaluator.EnsurePersonSettlementInitialized(person);
                if (person.settlementArrangement == SettlementArrangement.Departed)
                {
                    continue;
                }

                HouseholdState personHousehold = GetHousehold(state, person.householdId);
                person.settlementPressure.weeksInArrangement = personHousehold != null
                    ? personHousehold.settlementPressure.weeksInArrangement
                    : person.settlementPressure.weeksInArrangement + 1;
                AccrueSettlementSavings(person);
                if (person.settlementArrangement == SettlementArrangement.Transient
                    && TryMovePersonToBoarding(state, person, random, homeOptions, boardingHouseOptions, out bool playerOwnedBoardingHost))
                {
                    changes++;
                    state.lastWeeklySettlementMoveCount++;
                    if (playerOwnedBoardingHost)
                    {
                        state.lastWeeklyPlayerOwnedBoardingMoveCount++;
                    }

                    continue;
                }

            }

            int rentalMoves = ResolveRentalApplications(state, settings, homeOptions);
            changes += rentalMoves;
            state.lastWeeklySettlementMoveCount += rentalMoves;
            RefreshSettlementMetrics(state, homeOptions, boardingHouseOptions);
            state.lastWeeklySettlementActionSummary = BuildWeeklySettlementActionSummary(state, changes);
            return changes;
        }

        public static void RefreshSettlementMetrics(
            PopulationState state,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions = null,
            IReadOnlyList<NewcomerSettlementBoardingOption> boardingHouseOptions = null)
        {
            if (state == null)
            {
                return;
            }

            state.EnsureSettlementMetricsInitialized();
            homeOptions ??= Array.Empty<NewcomerSettlementHomeOption>();
            boardingHouseOptions ??= Array.Empty<NewcomerSettlementBoardingOption>();
            EnsureBoardingHouseHouseholds(state, boardingHouseOptions);

            int boardingCapacity = 0;
            int boardingUsed = 0;
            int boardingHouseCapacity = 0;
            int boardingHouseUsed = 0;
            int renters = 0;
            int kinPlacements = 0;
            int transients = 0;
            int unstableHouseholds = 0;
            int stableHouseholds = 0;

            if (state.households != null)
            {
                for (int i = 0; i < state.households.Count; i++)
                {
                    HouseholdState household = state.households[i];
                    if (household == null)
                    {
                        continue;
                    }

                    NewcomerSettlementEvaluator.RefreshHouseholdPressures(household);
                    boardingCapacity += Mathf.Max(0, household.boardingCapacity);
                    boardingUsed += household.boarderPersonIds != null ? household.boarderPersonIds.Count : 0;
                    if (household.isBoardingHouseGuestHousehold)
                    {
                        boardingHouseCapacity += Mathf.Max(0, household.boardingCapacity);
                        boardingHouseUsed += household.boarderPersonIds != null ? household.boarderPersonIds.Count : 0;
                    }

                    if (household.isRenterHousehold || household.settlementArrangement == SettlementArrangement.Renting)
                    {
                        renters++;
                    }

                    bool emptyBoardingHouseLodging = household.isBoardingHouseGuestHousehold
                        && (household.boarderPersonIds == null || household.boarderPersonIds.Count == 0);
                    if ((!emptyBoardingHouseLodging && NewcomerSettlementEvaluator.IsUnstable(household.settlementArrangement))
                        || household.crowdingPressure01 > 0.35f
                        || household.boarderOverloadPressure01 > 0f)
                    {
                        unstableHouseholds++;
                    }

                    if (household.settlementArrangement == SettlementArrangement.StableHousehold)
                    {
                        stableHouseholds++;
                    }

                    SyncMemberSettlementPressureFromHousehold(state, household);
                }
            }

            int laborEligible = 0;
            int laborReady = 0;
            if (state.people != null)
            {
                for (int i = 0; i < state.people.Count; i++)
                {
                    PersonState person = state.people[i];
                    if (person == null)
                    {
                        continue;
                    }

                    NewcomerSettlementEvaluator.EnsurePersonSettlementInitialized(person);
                    if (person.settlementArrangement == SettlementArrangement.Kin)
                    {
                        kinPlacements++;
                    }
                    else if (person.settlementArrangement == SettlementArrangement.Transient)
                    {
                        transients++;
                    }

                    if (person.laborAccessLevel >= LaborAccessLevel.YoungWorker
                        && person.settlementArrangement != SettlementArrangement.Departed)
                    {
                        laborEligible++;
                        if (NewcomerSettlementEvaluator.GetLaborReadiness01(person) >= 0.45f)
                        {
                            laborReady++;
                        }
                    }
                }
            }

            int homeCapacity = CountHomeCapacity(homeOptions);
            if (homeCapacity <= 0)
            {
                homeCapacity = CountDistinctActiveHomeBuildings(state);
            }

            int homeDemand = stableHouseholds + renters + Mathf.CeilToInt(transients / 2f);
            float homePressure = homeCapacity <= 0 ? 1f : Mathf.Clamp01((homeDemand - homeCapacity) / (float)Mathf.Max(1, homeCapacity));
            float boardingPressure = boardingCapacity <= 0
                ? boardingUsed > 0 ? 1f : 0f
                : Mathf.Clamp01(boardingUsed / (float)Mathf.Max(1, boardingCapacity));
            float transientPressure = Mathf.Clamp01(transients / (float)Mathf.Max(1, state.people != null ? state.people.Count : 1));

            state.boardingCapacity = boardingCapacity;
            state.boardingUsed = boardingUsed;
            state.boardingHouseCapacity = boardingHouseCapacity;
            state.boardingHouseUsed = boardingHouseUsed;
            state.renterHouseholdCount = renters;
            state.kinPlacementCount = kinPlacements;
            state.transientPersonCount = transients;
            state.unstableHouseholdCount = unstableHouseholds;
            state.housingPressure01 = Mathf.Clamp01(homePressure * 0.50f + boardingPressure * 0.30f + transientPressure * 0.20f);
            state.laborAbsorption01 = laborEligible <= 0 ? 1f : Mathf.Clamp01(laborReady / (float)laborEligible);
            RefreshRentalMetrics(state, homeOptions);
            state.settlementPressureSnapshot = BuildSettlementPressureSnapshot(state);
            SyncSettlementValidationMessages(state);
            state.lastSettlementSummary = BuildSettlementSummary(state);
        }

        private static void SyncMemberSettlementPressureFromHousehold(PopulationState state, HouseholdState household)
        {
            if (state == null || household == null || household.memberIds == null)
            {
                return;
            }

            for (int i = 0; i < household.memberIds.Count; i++)
            {
                PersonState person = state.GetPerson(household.memberIds[i]);
                if (person == null)
                {
                    continue;
                }

                NewcomerSettlementEvaluator.EnsurePersonSettlementInitialized(person);
                person.settlementPressure = SettlementPressureState.Create(
                    person.settlementArrangement,
                    Mathf.Min(person.settlementPressure.stability01, household.settlementPressure.stability01),
                    Mathf.Max(person.settlementPressure.housingFriction01, household.settlementPressure.housingFriction01),
                    Mathf.Min(person.settlementPressure.laborReadiness01, household.settlementPressure.laborReadiness01),
                    Mathf.Max(person.settlementPressure.weeksInArrangement, household.settlementPressure.weeksInArrangement),
                    Mathf.Max(person.settlementPressure.homeAmbition01, household.settlementPressure.homeAmbition01),
                    household.lastSettlementSummary);
                person.hostHouseholdId = household.id;
                if (person.homeBuildingId < 0)
                {
                    person.homeBuildingId = household.homeBuildingId;
                }
            }
        }

        public static void RefreshRentalMetrics(
            PopulationState state,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions = null)
        {
            if (state == null)
            {
                return;
            }

            state.EnsureSettlementMetricsInitialized();
            homeOptions ??= Array.Empty<NewcomerSettlementHomeOption>();
            Dictionary<int, int> occupied = BuildOccupiedHomeCounts(state);
            state.rentalProperties.Clear();
            int vacancyCount = 0;
            int playerOwnedVacancyCount = 0;

            for (int i = 0; i < homeOptions.Count; i++)
            {
                NewcomerSettlementHomeOption option = homeOptions[i];
                if (!option.RentalCapable || option.ResidentHouseholdCapacity <= 0 || IsBoardingHouseGuestBuilding(state, option.BuildingId))
                {
                    continue;
                }

                int used = occupied.TryGetValue(option.BuildingId, out int count) ? count : 0;
                int vacant = Mathf.Max(0, option.ResidentHouseholdCapacity - used);
                vacancyCount += vacant;
                if (option.PlayerOwned)
                {
                    playerOwnedVacancyCount += vacant;
                }

                RentalPropertyState property = new()
                {
                    buildingId = option.BuildingId,
                    displayName = option.DisplayName,
                    residentHouseholdCapacity = option.ResidentHouseholdCapacity,
                    occupiedHouseholds = Mathf.Clamp(used, 0, option.ResidentHouseholdCapacity),
                    vacantHouseholdSlots = vacant,
                    playerOwned = option.PlayerOwned,
                    mixedUse = option.MixedUse,
                    rentalCapable = option.RentalCapable,
                    occupancySummary = vacant > 0
                        ? $"{vacant}/{option.ResidentHouseholdCapacity} vacancy"
                        : $"occupied {used}/{option.ResidentHouseholdCapacity}"
                };
                property.Sanitize();
                state.rentalProperties.Add(property);
            }

            int housingNeed = CountStrongerHousingNeed(state);
            int waitingApplicants = CountWaitingRentalApplicants(state);
            state.rentalVacancyCount = vacancyCount;
            state.playerOwnedRentalVacancyCount = playerOwnedVacancyCount;
            state.rentalApplicantCount = waitingApplicants;
            state.strongerHousingNeedCount = housingNeed;
            state.rentalPressure01 = Mathf.Clamp01(housingNeed / (float)Mathf.Max(1, vacancyCount + 1));
            state.lastRentalSummary = BuildRentalSummary(state);
        }

        public static SettlementPressureSnapshot BuildSettlementPressureSnapshot(PopulationState state)
        {
            if (state == null)
            {
                return SettlementPressureSnapshot.Empty();
            }

            state.EnsureSettlementMetricsInitialized();
            int uncoveredHousingDemand = Mathf.Max(0, Mathf.Max(state.strongerHousingNeedCount, state.rentalApplicantCount) - state.rentalVacancyCount);
            float boardingSaturation = state.boardingCapacity <= 0 ? 0f : Mathf.Clamp01(state.boardingUsed / (float)Mathf.Max(1, state.boardingCapacity));
            float boardingHouseSaturation = state.boardingHouseCapacity <= 0 ? 0f : Mathf.Clamp01(state.boardingHouseUsed / (float)Mathf.Max(1, state.boardingHouseCapacity));
            float rentalShortage = Mathf.Clamp01(uncoveredHousingDemand / (float)Mathf.Max(1, state.strongerHousingNeedCount + state.rentalApplicantCount));
            float transientPressure = Mathf.Clamp01(state.transientPersonCount / (float)Mathf.Max(1, state.people != null ? state.people.Count : 1));
            float laborAbsorption = Mathf.Clamp01(state.laborAbsorption01);

            int score = 0;
            score += Mathf.RoundToInt(state.housingPressure01 * 30f);
            score += Mathf.RoundToInt(state.rentalPressure01 * 25f);
            score += Mathf.RoundToInt(boardingSaturation * 15f);
            score += Mathf.RoundToInt(transientPressure * 20f);
            score += Mathf.RoundToInt((1f - laborAbsorption) * 10f);
            if (uncoveredHousingDemand > 0)
            {
                score += Mathf.Min(25, uncoveredHousingDemand * 6);
            }

            SettlementPressureBand band = SettlementPressureBand.Calm;
            if ((state.transientPersonCount > 0 && uncoveredHousingDemand > 0) || score >= 75)
            {
                band = SettlementPressureBand.Urgent;
            }
            else if (uncoveredHousingDemand > 0 || state.transientPersonCount > 0 || score >= 45)
            {
                band = SettlementPressureBand.Pressure;
            }
            else if (state.rentalVacancyCount > 0 || state.boardingCapacity > state.boardingUsed || state.playerOwnedRentalVacancyCount > 0)
            {
                band = SettlementPressureBand.Opportunity;
            }

            string cause;
            string action;
            if (band == SettlementPressureBand.Urgent)
            {
                cause = uncoveredHousingDemand > 0
                    ? $"{uncoveredHousingDemand} housing need(s) have no matching rentable room."
                    : $"{state.transientPersonCount} transient resident(s) still lack a durable foothold.";
                action = "Prioritize rental rooms, boarding capacity, or player-owned lodging before more arrivals fail to settle.";
            }
            else if (band == SettlementPressureBand.Pressure)
            {
                cause = uncoveredHousingDemand > 0
                    ? "Housing demand is outpacing open rental capacity."
                    : state.transientPersonCount > 0 ? "Transient residents still need a steadier bridge into town." : "Boarding, rental, or labor absorption is tightening.";
                action = "Watch boarding saturation and prepare rental or lodging relief.";
            }
            else if (band == SettlementPressureBand.Opportunity)
            {
                cause = state.playerOwnedRentalVacancyCount > 0
                    ? $"{state.playerOwnedRentalVacancyCount} player-owned rentable room(s) can absorb newcomers."
                    : "Open lodging capacity can support more settlement.";
                action = "Use available rooms to absorb useful newcomers before pressure rises.";
            }
            else
            {
                cause = "Boarding and rental pressure are currently contained.";
                action = string.Empty;
            }

            return new SettlementPressureSnapshot
            {
                pressureBand = band,
                pressureScore = Mathf.Clamp(score, 0, 100),
                boardingSaturation01 = boardingSaturation,
                boardingHouseSaturation01 = boardingHouseSaturation,
                rentalShortage01 = rentalShortage,
                transientPressure01 = transientPressure,
                laborAbsorption01 = laborAbsorption,
                boardingCapacity = state.boardingCapacity,
                boardingUsed = state.boardingUsed,
                rentalVacancyCount = state.rentalVacancyCount,
                playerOwnedRentalVacancyCount = state.playerOwnedRentalVacancyCount,
                rentalApplicantCount = state.rentalApplicantCount,
                strongerHousingNeedCount = state.strongerHousingNeedCount,
                transientPersonCount = state.transientPersonCount,
                uncoveredHousingDemand = uncoveredHousingDemand,
                playerOwnedVacancyReliefAvailable = state.playerOwnedRentalVacancyCount > 0,
                headline = $"Settlement {band} ({Mathf.Clamp(score, 0, 100)}/100).",
                primaryCause = cause,
                recommendedAction = action
            };
        }

        public static void SyncSettlementValidationMessages(PopulationState state)
        {
            if (state == null)
            {
                return;
            }

            state.EnsureSettlementMetricsInitialized();
            state.populationValidationSnapshot = BuildPopulationValidationSnapshot(state);
            state.lastPopulationValidationSummary = state.populationValidationSnapshot.headline ?? string.Empty;
            state.validationMessages ??= new List<string>();
            for (int i = state.validationMessages.Count - 1; i >= 0; i--)
            {
                string message = state.validationMessages[i];
                if (string.IsNullOrWhiteSpace(message) || message.IndexOf("[PopulationDiagnostics]", StringComparison.Ordinal) >= 0)
                {
                    state.validationMessages.RemoveAt(i);
                }
            }

            if (state.populationValidationSnapshot.severity != PopulationValidationSeverity.Clean)
            {
                string prefix = state.populationValidationSnapshot.severity == PopulationValidationSeverity.Error ? "ERROR" : state.populationValidationSnapshot.severity == PopulationValidationSeverity.Warning ? "WARN" : "NOTICE";
                state.validationMessages.Add($"{prefix}: [PopulationDiagnostics] {state.populationValidationSnapshot.headline} {state.populationValidationSnapshot.detail}");
            }
        }

        public static PopulationValidationSnapshot BuildPopulationValidationSnapshot(PopulationState state)
        {
            PopulationValidationSnapshot snapshot = PopulationValidationSnapshot.Clean();
            if (state == null)
            {
                snapshot.severity = PopulationValidationSeverity.Error;
                snapshot.errorCount = 1;
                snapshot.headline = "Population state is missing.";
                return snapshot;
            }

            state.people ??= new List<PersonState>();
            state.households ??= new List<HouseholdState>();
            snapshot.personCount = state.people.Count;
            snapshot.householdCount = state.households.Count;
            Dictionary<int, PersonState> peopleById = new();
            HashSet<int> householdIds = new();
            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                if (person == null)
                {
                    snapshot.nullPersonEntries++;
                    continue;
                }

                if (person.id < 0)
                {
                    snapshot.negativePersonIds++;
                }

                if (peopleById.ContainsKey(person.id))
                {
                    snapshot.duplicatePersonIds++;
                }
                else
                {
                    peopleById[person.id] = person;
                }

                if (person.settlementArrangement != SettlementArrangement.Departed)
                {
                    snapshot.activePersonCount++;
                }

                if (person.householdId >= 0 && state.GetHousehold(person.householdId) == null)
                {
                    snapshot.orphanedPeople++;
                }

                if (person.settlementArrangement != SettlementArrangement.Departed
                    && person.laborAccessLevel >= LaborAccessLevel.YoungWorker
                    && person.workplaceBuildingId < 0)
                {
                    snapshot.unassignedLaborEligibleWorkers++;
                }

                if (person.startingCashCents < 0)
                {
                    snapshot.negativeMoneyRecords++;
                }
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household == null)
                {
                    snapshot.nullHouseholdEntries++;
                    continue;
                }

                if (household.id < 0)
                {
                    snapshot.negativeHouseholdIds++;
                }

                if (!householdIds.Add(household.id))
                {
                    snapshot.duplicateHouseholdIds++;
                }

                if (household.settlementArrangement != SettlementArrangement.Departed)
                {
                    snapshot.activeHouseholdCount++;
                }

                household.memberIds ??= new List<int>();
                household.boarderPersonIds ??= new List<int>();
                if (household.memberIds.Count == 0
                    && !household.isBoardingHouseGuestHousehold
                    && household.settlementArrangement != SettlementArrangement.Departed)
                {
                    snapshot.emptyNonGuestHouseholds++;
                }

                HashSet<int> localMembers = new();
                for (int memberIndex = 0; memberIndex < household.memberIds.Count; memberIndex++)
                {
                    int personId = household.memberIds[memberIndex];
                    if (!localMembers.Add(personId))
                    {
                        snapshot.duplicateMemberLinks++;
                    }

                    if (!peopleById.TryGetValue(personId, out PersonState person))
                    {
                        snapshot.missingMemberReferences++;
                    }
                    else if (person.householdId != household.id)
                    {
                        snapshot.householdMemberMismatches++;
                    }
                }

                HashSet<int> localBoarders = new();
                for (int boarderIndex = 0; boarderIndex < household.boarderPersonIds.Count; boarderIndex++)
                {
                    int personId = household.boarderPersonIds[boarderIndex];
                    if (!localBoarders.Add(personId))
                    {
                        snapshot.duplicateBoarderLinks++;
                    }

                    if (!peopleById.TryGetValue(personId, out PersonState person))
                    {
                        snapshot.missingBoarderReferences++;
                    }
                    else if (person.settlementArrangement != SettlementArrangement.Boarding)
                    {
                        snapshot.boarderArrangementMismatches++;
                    }
                }

                if (household.boardingCapacity > 0 && household.boarderPersonIds.Count > household.boardingCapacity)
                {
                    snapshot.boardingOverCapacityHouseholds++;
                }

                if (household.spendingMoneyCents < 0 || household.weeklyIncomeSnapshot < 0)
                {
                    snapshot.negativeMoneyRecords++;
                }
            }

            snapshot.errorCount = snapshot.nullPersonEntries + snapshot.nullHouseholdEntries + snapshot.duplicatePersonIds + snapshot.duplicateHouseholdIds + snapshot.orphanedPeople + snapshot.missingMemberReferences + snapshot.missingBoarderReferences + snapshot.householdMemberMismatches;
            snapshot.warningCount = snapshot.emptyNonGuestHouseholds + snapshot.boarderArrangementMismatches + snapshot.boardingOverCapacityHouseholds + snapshot.negativeMoneyRecords + snapshot.negativePersonIds + snapshot.negativeHouseholdIds + snapshot.duplicateMemberLinks + snapshot.duplicateBoarderLinks;
            snapshot.noticeCount = snapshot.unassignedLaborEligibleWorkers;
            if (snapshot.errorCount > 0)
            {
                snapshot.severity = PopulationValidationSeverity.Error;
                snapshot.headline = "Population records have blocking reference errors.";
                snapshot.detail = $"errors {snapshot.errorCount}, warnings {snapshot.warningCount}, notices {snapshot.noticeCount}";
                snapshot.recommendedAction = "Repair references before trusting settlement output.";
            }
            else if (snapshot.warningCount > 0)
            {
                snapshot.severity = PopulationValidationSeverity.Warning;
                snapshot.headline = "Population records need cleanup.";
                snapshot.detail = $"warnings {snapshot.warningCount}, notices {snapshot.noticeCount}";
                snapshot.recommendedAction = "Review boarding, household membership, and money records.";
            }
            else if (snapshot.noticeCount > 0)
            {
                snapshot.severity = PopulationValidationSeverity.Notice;
                snapshot.headline = "Population records are valid with operational notices.";
                snapshot.detail = $"unassigned labor-ready workers {snapshot.unassignedLaborEligibleWorkers}";
                snapshot.recommendedAction = "Use staffing or settlement systems to absorb available labor.";
            }

            return snapshot;
        }

        public static int GetBoardingHouseOccupancy(PopulationState state, string businessInstanceId, int buildingId)
        {
            HouseholdState household = FindBoardingHouseGuestHousehold(state, businessInstanceId, buildingId);
            return household != null && household.boarderPersonIds != null ? household.boarderPersonIds.Count : 0;
        }

        public static NewcomerProfileDefinition GetProfileDefinition(NewcomerArrivalProfile profile)
        {
            return profile switch
            {
                NewcomerArrivalProfile.LoneLaborer => LoneLaborer,
                NewcomerArrivalProfile.KinLinkedArrival => KinLinked,
                NewcomerArrivalProfile.BoarderSeekingWorker => BoarderSeekingWorker,
                NewcomerArrivalProfile.RenterReadyHousehold => RenterReady,
                NewcomerArrivalProfile.SkilledCapitalizedArrival => SkilledCapitalized,
                NewcomerArrivalProfile.DistressedRelocationHousehold => DistressedRelocation,
                NewcomerArrivalProfile.WidowElderDependentRelocation => WidowElderRelocation,
                _ => SkilledCapitalized
            };
        }

        private static int PlaceArrival(
            PopulationState state,
            PopulationGenerationSettings settings,
            System.Random random,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions,
            NewcomerProfileDefinition profile)
        {
            switch (profile.Profile)
            {
                case NewcomerArrivalProfile.KinLinkedArrival:
                    if (TryAddKinArrival(state, settings, random, profile, out int kinPeople))
                    {
                        return kinPeople;
                    }

                    if (TryCreateRenterOrStableHousehold(state, settings, random, homeOptions, profile, SettlementArrangement.Renting, out int kinFallbackRenters))
                    {
                        return kinFallbackRenters;
                    }

                    return CreateTransientOrDeparted(state, settings, random, homeOptions, profile);

                case NewcomerArrivalProfile.BoarderSeekingWorker:
                case NewcomerArrivalProfile.LoneLaborer:
                    if (TryAddBoarder(state, settings, random, profile, out int boarderPeople))
                    {
                        return boarderPeople;
                    }

                    if (random.NextDouble() < profile.RentingPreference01
                        && TryCreateRenterOrStableHousehold(state, settings, random, homeOptions, profile, SettlementArrangement.Renting, out int workerRenterPeople))
                    {
                        return workerRenterPeople;
                    }

                    return CreateTransientOrDeparted(state, settings, random, homeOptions, profile);

                case NewcomerArrivalProfile.RenterReadyHousehold:
                    if (TryCreateRenterOrStableHousehold(state, settings, random, homeOptions, profile, SettlementArrangement.Renting, out int renterPeople))
                    {
                        return renterPeople;
                    }

                    if (TryAddBoarder(state, settings, random, profile, out int renterBoarderPeople))
                    {
                        return renterBoarderPeople;
                    }

                    return CreateTransientOrDeparted(state, settings, random, homeOptions, profile);

                case NewcomerArrivalProfile.SkilledCapitalizedArrival:
                    if (TryCreateRenterOrStableHousehold(state, settings, random, homeOptions, profile, SettlementArrangement.StableHousehold, out int skilledPeople))
                    {
                        return skilledPeople;
                    }

                    if (TryAddBoarder(state, settings, random, profile, out int skilledBoarderPeople))
                    {
                        return skilledBoarderPeople;
                    }

                    return CreateTransientOrDeparted(state, settings, random, homeOptions, profile);

                case NewcomerArrivalProfile.WidowElderDependentRelocation:
                    if (TryAddKinArrival(state, settings, random, profile, out int elderKinPeople))
                    {
                        return elderKinPeople;
                    }

                    if (TryAddBoarder(state, settings, random, profile, out int elderBoarderPeople))
                    {
                        return elderBoarderPeople;
                    }

                    return CreateTransientOrDeparted(state, settings, random, homeOptions, profile);

                default:
                    if (TryCreateRenterOrStableHousehold(state, settings, random, homeOptions, profile, SettlementArrangement.Renting, out int distressedRenters))
                    {
                        return distressedRenters;
                    }

                    if (TryAddKinArrival(state, settings, random, profile, out int distressedKinPeople))
                    {
                        return distressedKinPeople;
                    }

                    if (TryAddBoarder(state, settings, random, profile, out int distressedBoarders))
                    {
                        return distressedBoarders;
                    }

                    return CreateTransientOrDeparted(state, settings, random, homeOptions, profile);
            }
        }

        private static bool TryCreateRenterOrStableHousehold(
            PopulationState state,
            PopulationGenerationSettings settings,
            System.Random random,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions,
            NewcomerProfileDefinition profile,
            SettlementArrangement arrangement,
            out int createdPeople)
        {
            createdPeople = 0;
            if (!TryFindVacantHome(state, homeOptions, out NewcomerSettlementHomeOption home))
            {
                return false;
            }

            int householdSize = RollHouseholdSize(profile, random);
            HouseholdState household = CreateHouseholdShell(state, settings, random, profile, arrangement, home.BuildingId, home.BaseBoardingCapacity, householdSize);
            for (int i = 0; i < householdSize; i++)
            {
                PersonState person = CreateProfilePerson(state, settings, random, profile, arrangement, household.id, household.surname, home.BuildingId, i, -1, household.id);
                household.memberIds.Add(person.id);
                state.people.Add(person);
            }

            household.isRenterHousehold = arrangement == SettlementArrangement.Renting;
            household.EnsureHouseholdReservesInitialized(profile.Profile == NewcomerArrivalProfile.SkilledCapitalizedArrival);
            ApplyStartingReserves(household, profile);
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(household);
            state.households.Add(household);
            createdPeople = householdSize;
            return true;
        }

        private static bool TryAddBoarder(
            PopulationState state,
            PopulationGenerationSettings settings,
            System.Random random,
            NewcomerProfileDefinition profile,
            out int createdPeople)
        {
            createdPeople = 0;
            int householdSize = RollHouseholdSize(profile, random);
            if (householdSize != 1)
            {
                return false;
            }

            HouseholdState host = PickBoardingHost(state, random);
            if (host == null)
            {
                return false;
            }

            NewcomerSettlementEvaluator.EnsureHouseholdSettlementInitialized(host);
            PersonState person = CreateProfilePerson(
                state,
                settings,
                random,
                profile,
                SettlementArrangement.Boarding,
                host.id,
                PickBoarderSurname(settings, host, profile, random),
                host.homeBuildingId,
                0,
                -1,
                host.id);
            host.memberIds.Add(person.id);
            host.boarderPersonIds.Add(person.id);
            host.hostsBoarders = true;
            host.spendingMoneyCents += Mathf.RoundToInt(Mathf.Max(0, profile.MinCashCents) * 0.10f);
            state.people.Add(person);
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(host);
            createdPeople = 1;
            return true;
        }

        private static bool TryAddKinArrival(
            PopulationState state,
            PopulationGenerationSettings settings,
            System.Random random,
            NewcomerProfileDefinition profile,
            out int createdPeople)
        {
            createdPeople = 0;
            HouseholdState host = PickKinHost(state, random);
            if (host == null)
            {
                return false;
            }

            int householdSize = RollHouseholdSize(profile, random);
            for (int i = 0; i < householdSize; i++)
            {
                int forcedAge = profile.Profile == NewcomerArrivalProfile.WidowElderDependentRelocation && i == 0
                    ? random.Next(54, 79)
                    : -1;
                PersonState person = CreateProfilePerson(
                    state,
                    settings,
                    random,
                    profile,
                    SettlementArrangement.Kin,
                    host.id,
                    host.surname,
                    host.homeBuildingId,
                    i,
                    forcedAge,
                    host.id);
                host.memberIds.Add(person.id);
                state.people.Add(person);
                createdPeople++;
            }

            host.hasKinAbsorptionPressure = true;
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(host);
            return createdPeople > 0;
        }

        private static int CreateTransientOrDeparted(
            PopulationState state,
            PopulationGenerationSettings settings,
            System.Random random,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions,
            NewcomerProfileDefinition profile)
        {
            int householdSize = RollHouseholdSize(profile, random);
            bool severePressure = state.housingPressure01 >= settings.departureHousingPressureThreshold;
            if (severePressure && random.NextDouble() < settings.pressuredArrivalDepartureChance)
            {
                state.departedUnsettledCount += householdSize;
                return 0;
            }

            int fallbackHome = PickFallbackHomeBuildingId(state, homeOptions);
            if (fallbackHome < 0)
            {
                state.departedUnsettledCount += householdSize;
                return 0;
            }

            string surname = PopulationGenerator.Pick(settings.surnames, random, "Hale");
            HouseholdState household = CreateHouseholdShell(state, settings, random, profile, SettlementArrangement.Transient, fallbackHome, 0, householdSize, surname);
            household.isTransientHousehold = true;
            household.dwellingKind = HouseholdDwellingKind.RoughTemporaryLodging;
            household.dwellingSummary = "Rough temporary lodging / tent fallback";
            for (int i = 0; i < householdSize; i++)
            {
                PersonState person = CreateProfilePerson(state, settings, random, profile, SettlementArrangement.Transient, household.id, surname, fallbackHome, i, -1, household.id);
                household.memberIds.Add(person.id);
                state.people.Add(person);
            }

            household.EnsureHouseholdReservesInitialized(false);
            ApplyStartingReserves(household, profile);
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(household);
            state.households.Add(household);
            return householdSize;
        }

        private static bool TryMovePersonToBoarding(
            PopulationState state,
            PersonState person,
            System.Random random,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions,
            IReadOnlyList<NewcomerSettlementBoardingOption> boardingHouseOptions,
            out bool playerOwnedHost)
        {
            playerOwnedHost = false;
            if (state == null || person == null)
            {
                return false;
            }

            HouseholdState current = GetHousehold(state, person.householdId);
            HouseholdState host = PickBoardingHost(state, random, current != null ? current.id : -1, homeOptions, boardingHouseOptions);
            if (host == null)
            {
                return false;
            }

            playerOwnedHost = IsBoardingHostPlayerOwned(host, homeOptions, boardingHouseOptions);
            RemovePersonFromHousehold(current, person.id);
            person.householdId = host.id;
            person.hostHouseholdId = host.id;
            person.homeBuildingId = host.homeBuildingId;
            person.currentDestinationBuildingId = host.homeBuildingId;
            person.settlementArrangement = SettlementArrangement.Boarding;
            person.settlementPressure = SettlementPressureState.Create(
                SettlementArrangement.Boarding,
                NewcomerSettlementEvaluator.GetDefaultSettlementStability(SettlementArrangement.Boarding),
                NewcomerSettlementEvaluator.GetDefaultHousingFriction(SettlementArrangement.Boarding),
                NewcomerSettlementEvaluator.GetDefaultLaborReadiness(SettlementArrangement.Boarding),
                0,
                NewcomerSettlementEvaluator.GetDefaultHomeAmbition(SettlementArrangement.Boarding),
                playerOwnedHost ? "Moved from transient lodging to player-owned boarding" : "Moved from transient lodging to boarding");
            host.memberIds.Add(person.id);
            host.boarderPersonIds.Add(person.id);
            host.hostsBoarders = true;
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(host);
            if (current != null)
            {
                NewcomerSettlementEvaluator.RefreshHouseholdPressures(current);
                RemoveEmptyTemporaryHousehold(state, current);
            }

            return true;
        }

        private static bool TryMoveSinglePersonToVacantHome(
            PopulationState state,
            PersonState person,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions,
            SettlementArrangement arrangement,
            bool renter)
        {
            if (state == null || person == null || !TryFindVacantHome(state, homeOptions, out NewcomerSettlementHomeOption home))
            {
                return false;
            }

            return TryMoveSinglePersonToVacantHome(state, person, home, arrangement, renter, renter ? "Moved from boarding to rented rooms" : "Formed stable household");
        }

        private static bool TryMoveSinglePersonToVacantHome(
            PopulationState state,
            PersonState person,
            NewcomerSettlementHomeOption home,
            SettlementArrangement arrangement,
            bool renter,
            string summary)
        {
            if (state == null || person == null || home.BuildingId < 0)
            {
                return false;
            }

            HouseholdState current = GetHousehold(state, person.householdId);
            RemovePersonFromHousehold(current, person.id);

            int newHouseholdId = GetNextHouseholdId(state);
            HouseholdState household = new()
            {
                id = newHouseholdId,
                surname = person.lastName,
                householdName = $"{person.lastName} Household",
                homeBuildingId = home.BuildingId,
                arrivalProfile = person.arrivalProfile,
                settlementArrangement = arrangement,
                dwellingKind = ResolveDwellingForArrangement(arrangement),
                dwellingSummary = NewcomerSettlementEvaluator.GetDwellingDisplayName(ResolveDwellingForArrangement(arrangement)),
                isRenterHousehold = renter,
                baseBoardingCapacity = home.BaseBoardingCapacity,
                spendingMoneyCents = Mathf.Max(750, person.startingCashCents / 2),
                settlementReserveStrengthCents = Mathf.Max(0, person.startingCashCents / 2),
                demandSnapshot = HouseholdDemandSnapshot.Empty()
            };

            person.householdId = newHouseholdId;
            person.hostHouseholdId = newHouseholdId;
            person.homeBuildingId = home.BuildingId;
            person.currentDestinationBuildingId = home.BuildingId;
            person.settlementArrangement = arrangement;
            person.settlementPressure = SettlementPressureState.Create(
                arrangement,
                NewcomerSettlementEvaluator.GetDefaultSettlementStability(arrangement),
                NewcomerSettlementEvaluator.GetDefaultHousingFriction(arrangement),
                NewcomerSettlementEvaluator.GetDefaultLaborReadiness(arrangement),
                0,
                NewcomerSettlementEvaluator.GetDefaultHomeAmbition(arrangement),
                string.IsNullOrWhiteSpace(summary)
                    ? renter ? "Moved from boarding to rented rooms" : "Formed stable household"
                    : summary);
            household.memberIds.Add(person.id);
            household.EnsureHouseholdReservesInitialized(false);
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(household);
            state.households.Add(household);

            if (current != null)
            {
                NewcomerSettlementEvaluator.RefreshHouseholdPressures(current);
            }

            return true;
        }

        private static bool TryMoveHouseholdToVacantHome(
            PopulationState state,
            HouseholdState household,
            NewcomerSettlementHomeOption home,
            SettlementArrangement arrangement,
            bool renter,
            string summary)
        {
            if (state == null || household == null || home.BuildingId < 0)
            {
                return false;
            }

            household.homeBuildingId = home.BuildingId;
            household.settlementArrangement = arrangement;
            household.dwellingKind = ResolveDwellingForArrangement(arrangement);
            household.dwellingSummary = NewcomerSettlementEvaluator.GetDwellingDisplayName(household.dwellingKind);
            household.isRenterHousehold = renter;
            household.hasKinAbsorptionPressure = false;
            household.isTransientHousehold = false;
            household.hostsBoarders = false;
            household.boarderPersonIds?.Clear();
            household.baseBoardingCapacity = home.BaseBoardingCapacity;
            household.boardingCapacity = home.BaseBoardingCapacity;
            household.lastSettlementSummary = string.IsNullOrWhiteSpace(summary)
                ? renter ? "Household accepted rented rooms" : "Household secured stable rooms"
                : summary;
            household.settlementPressure = SettlementPressureState.Create(
                arrangement,
                NewcomerSettlementEvaluator.GetDefaultSettlementStability(arrangement),
                NewcomerSettlementEvaluator.GetDefaultHousingFriction(arrangement),
                NewcomerSettlementEvaluator.GetDefaultLaborReadiness(arrangement),
                0,
                NewcomerSettlementEvaluator.GetDefaultHomeAmbition(arrangement),
                household.lastSettlementSummary);

            if (household.memberIds != null)
            {
                for (int i = 0; i < household.memberIds.Count; i++)
                {
                    PersonState person = state.GetPerson(household.memberIds[i]);
                    if (person == null)
                    {
                        continue;
                    }

                    person.householdId = household.id;
                    person.hostHouseholdId = household.id;
                    person.homeBuildingId = home.BuildingId;
                    person.currentDestinationBuildingId = home.BuildingId;
                    person.settlementArrangement = arrangement;
                    person.settlementPressure = SettlementPressureState.Create(
                        arrangement,
                        NewcomerSettlementEvaluator.GetDefaultSettlementStability(arrangement),
                        NewcomerSettlementEvaluator.GetDefaultHousingFriction(arrangement),
                        NewcomerSettlementEvaluator.GetDefaultLaborReadiness(arrangement),
                        0,
                        NewcomerSettlementEvaluator.GetDefaultHomeAmbition(arrangement),
                        string.IsNullOrWhiteSpace(summary)
                            ? (renter ? "Household moved into rented rooms" : "Household secured stable rooms")
                            : summary);
                }
            }

            NewcomerSettlementEvaluator.RefreshHouseholdPressures(household);
            return true;
        }

        private static HouseholdState CreateHouseholdShell(
            PopulationState state,
            PopulationGenerationSettings settings,
            System.Random random,
            NewcomerProfileDefinition profile,
            SettlementArrangement arrangement,
            int homeBuildingId,
            int baseBoardingCapacity,
            int householdSize,
            string forcedSurname = null)
        {
            string surname = string.IsNullOrWhiteSpace(forcedSurname)
                ? PopulationGenerator.Pick(settings.surnames, random, "Hale")
                : forcedSurname;
            int reserve = random.Next(profile.MinCashCents, profile.MaxCashCents + 1);
            return new HouseholdState
            {
                id = GetNextHouseholdId(state),
                surname = surname,
                householdName = $"{surname} Household",
                homeBuildingId = homeBuildingId,
                spendingMoneyCents = Mathf.Max(250, reserve),
                settlementReserveStrengthCents = reserve,
                arrivalProfile = profile.Profile,
                settlementArrangement = arrangement,
                dwellingKind = ResolveDwellingForArrangement(arrangement),
                dwellingSummary = NewcomerSettlementEvaluator.GetDwellingDisplayName(ResolveDwellingForArrangement(arrangement)),
                baseBoardingCapacity = Mathf.Max(0, baseBoardingCapacity),
                demandSnapshot = HouseholdDemandSnapshot.Empty(),
                isTransientHousehold = arrangement == SettlementArrangement.Transient,
                isRenterHousehold = arrangement == SettlementArrangement.Renting,
                lastSettlementSummary = $"{NewcomerSettlementEvaluator.GetProfileDisplayName(profile.Profile)} arrived as {NewcomerSettlementEvaluator.GetArrangementDisplayName(arrangement)}"
            };
        }

        private static HouseholdDwellingKind ResolveDwellingForArrangement(SettlementArrangement arrangement)
        {
            return arrangement switch
            {
                SettlementArrangement.Renting => HouseholdDwellingKind.RentedHomeOrRoom,
                SettlementArrangement.Boarding => HouseholdDwellingKind.BoardingHouseLodging,
                SettlementArrangement.Kin => HouseholdDwellingKind.KinSharedHousehold,
                SettlementArrangement.Transient => HouseholdDwellingKind.RoughTemporaryLodging,
                SettlementArrangement.Departed => HouseholdDwellingKind.Departed,
                _ => HouseholdDwellingKind.OwnedHome
            };
        }

        private static PersonState CreateProfilePerson(
            PopulationState state,
            PopulationGenerationSettings settings,
            System.Random random,
            NewcomerProfileDefinition profile,
            SettlementArrangement arrangement,
            int householdId,
            string surname,
            int homeBuildingId,
            int memberIndex,
            int forcedAge,
            int hostHouseholdId)
        {
            int personId = state.people != null ? state.people.Count : 0;
            PersonState person = PopulationGenerator.CreateSettlementPerson(
                personId,
                householdId,
                surname,
                homeBuildingId,
                memberIndex,
                settings,
                random,
                profile.Profile,
                arrangement,
                forcedAge,
                profile.LaborUrgency01,
                ResolveReadinessModifier(profile, arrangement),
                profile.ProfessionBias,
                profile.SettlementDifficulty,
                hostHouseholdId,
                random.Next(profile.MinCashCents, profile.MaxCashCents + 1));
            ApplyProfileWorkerTraitNudge(person, profile);
            return person;
        }

        private static NewcomerProfileDefinition RollProfile(PopulationGenerationSettings settings, System.Random random)
        {
            int totalWeight =
                settings.loneLaborerArrivalWeight
                + settings.kinLinkedArrivalWeight
                + settings.boarderSeekingWorkerArrivalWeight
                + settings.renterReadyHouseholdArrivalWeight
                + settings.skilledCapitalizedArrivalWeight
                + settings.distressedRelocationArrivalWeight
                + settings.widowElderRelocationArrivalWeight;
            if (totalWeight <= 0)
            {
                return LoneLaborer;
            }

            int roll = random.Next(0, totalWeight);
            if ((roll -= settings.loneLaborerArrivalWeight) < 0)
            {
                return LoneLaborer;
            }

            if ((roll -= settings.kinLinkedArrivalWeight) < 0)
            {
                return KinLinked;
            }

            if ((roll -= settings.boarderSeekingWorkerArrivalWeight) < 0)
            {
                return BoarderSeekingWorker;
            }

            if ((roll -= settings.renterReadyHouseholdArrivalWeight) < 0)
            {
                return RenterReady;
            }

            if ((roll -= settings.skilledCapitalizedArrivalWeight) < 0)
            {
                return SkilledCapitalized;
            }

            if ((roll -= settings.distressedRelocationArrivalWeight) < 0)
            {
                return DistressedRelocation;
            }

            return WidowElderRelocation;
        }

        private static int RollHouseholdSize(NewcomerProfileDefinition profile, System.Random random)
        {
            return random.Next(profile.MinHouseholdSize, profile.MaxHouseholdSize + 1);
        }

        private static void InitializeExistingHouseholds(PopulationState state, IReadOnlyList<NewcomerSettlementHomeOption> homeOptions)
        {
            if (state.households == null)
            {
                return;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household == null)
                {
                    continue;
                }

                if (household.arrivalProfile == NewcomerArrivalProfile.SettledResident
                    && household.settlementArrangement == SettlementArrangement.StableHousehold)
                {
                    household.baseBoardingCapacity = FindHomeOption(homeOptions, household.homeBuildingId, out NewcomerSettlementHomeOption option)
                        ? option.BaseBoardingCapacity
                        : household.baseBoardingCapacity;
                    household.settlementReserveStrengthCents = Mathf.Max(household.settlementReserveStrengthCents, household.spendingMoneyCents);
                }

                NewcomerSettlementEvaluator.RefreshHouseholdPressures(household);
            }
        }

        private static void ApplyStartingReserves(HouseholdState household, NewcomerProfileDefinition profile)
        {
            if (household == null)
            {
                return;
            }

            household.EnsureHouseholdReservesInitialized(false);
            HouseholdReserveState staple = household.GetReserve(HouseholdReserveCatalog.StapleFoodCategoryId);
            if (staple == null)
            {
                return;
            }

            float reserveShare = profile.Profile switch
            {
                NewcomerArrivalProfile.SkilledCapitalizedArrival => 0.85f,
                NewcomerArrivalProfile.RenterReadyHousehold => 0.55f,
                NewcomerArrivalProfile.DistressedRelocationHousehold => 0.12f,
                NewcomerArrivalProfile.WidowElderDependentRelocation => 0.25f,
                _ => 0.30f
            };
            staple.currentUnits = Mathf.Clamp(Mathf.RoundToInt(staple.targetUnits * reserveShare), 0, staple.targetUnits);
            household.SyncLegacyFoodReserveFromReserves();
        }

        private static void ApplyProfileWorkerTraitNudge(PersonState person, NewcomerProfileDefinition profile)
        {
            if (person == null)
            {
                return;
            }

            person.EnsureWorkerTraitsInitialized();
            WorkerHiddenTraits traits = person.hiddenWorkerTraits;
            switch (profile.Profile)
            {
                case NewcomerArrivalProfile.LoneLaborer:
                case NewcomerArrivalProfile.BoarderSeekingWorker:
                    traits.durability = WorkerHiddenTraits.ClampTrait(traits.durability + 8);
                    traits.reliability = WorkerHiddenTraits.ClampTrait(traits.reliability + 3);
                    break;
                case NewcomerArrivalProfile.SkilledCapitalizedArrival:
                    traits.learningSpeed = WorkerHiddenTraits.ClampTrait(traits.learningSpeed + 8);
                    traits.care = WorkerHiddenTraits.ClampTrait(traits.care + 6);
                    person.visibleWorkerProfile.generalSkill = WorkerHiddenTraits.ClampTrait(person.visibleWorkerProfile.generalSkill + 12);
                    break;
                case NewcomerArrivalProfile.DistressedRelocationHousehold:
                    traits.reliability = WorkerHiddenTraits.ClampTrait(traits.reliability + 5);
                    traits.durability = WorkerHiddenTraits.ClampTrait(traits.durability - 3);
                    break;
                case NewcomerArrivalProfile.WidowElderDependentRelocation:
                    traits.care = WorkerHiddenTraits.ClampTrait(traits.care + 8);
                    traits.durability = WorkerHiddenTraits.ClampTrait(traits.durability - 10);
                    break;
            }
        }

        private static int ResolveReadinessModifier(NewcomerProfileDefinition profile, SettlementArrangement arrangement)
        {
            int modifier = profile.Profile switch
            {
                NewcomerArrivalProfile.SkilledCapitalizedArrival => 12,
                NewcomerArrivalProfile.RenterReadyHousehold => 6,
                NewcomerArrivalProfile.BoarderSeekingWorker => 4,
                NewcomerArrivalProfile.LoneLaborer => 2,
                NewcomerArrivalProfile.DistressedRelocationHousehold => -8,
                NewcomerArrivalProfile.WidowElderDependentRelocation => -12,
                _ => 0
            };

            modifier += arrangement switch
            {
                SettlementArrangement.StableHousehold => 8,
                SettlementArrangement.Renting => 4,
                SettlementArrangement.Transient => -18,
                _ => 0
            };
            return modifier;
        }

        private static void ResetWeeklySettlementOutcome(PopulationState state)
        {
            if (state == null)
            {
                return;
            }

            state.lastWeeklySettlementMoveCount = 0;
            state.lastWeeklyPlayerOwnedRentalMoveCount = 0;
            state.lastWeeklyPlayerOwnedBoardingMoveCount = 0;
            state.lastWeeklySettlementActionSummary = "No settlement moves completed this week.";
        }

        private static string BuildWeeklySettlementActionSummary(PopulationState state, int movedResidents)
        {
            if (state == null)
            {
                return string.Empty;
            }

            if (movedResidents <= 0)
            {
                if (state.rentalApplicantCount > 0 && state.rentalVacancyCount <= 0)
                {
                    return $"No settlement moves completed; {state.rentalApplicantCount} applicant(s) are waiting for rentable rooms.";
                }

                if (state.playerOwnedRentalVacancyCount > 0 && state.rentalApplicantCount <= 0)
                {
                    return $"No settlement moves completed; {state.playerOwnedRentalVacancyCount} player-owned rentable room(s) remain open for future arrivals.";
                }

                if (state.transientPersonCount > 0)
                {
                    return $"No settlement moves completed; {state.transientPersonCount} transient resident(s) still need a better foothold.";
                }

                return "No settlement moves completed this week.";
            }

            List<string> parts = new()
            {
                $"Moved {movedResidents} resident(s) into better settlement arrangements"
            };

            int playerOwnedMoves = Mathf.Max(0, state.lastWeeklyPlayerOwnedRentalMoveCount + state.lastWeeklyPlayerOwnedBoardingMoveCount);
            if (playerOwnedMoves > 0)
            {
                parts.Add($"player-owned rooms absorbed {playerOwnedMoves}");
            }

            if (state.lastWeeklyPlayerOwnedRentalMoveCount > 0 && state.lastWeeklyPlayerOwnedBoardingMoveCount > 0)
            {
                parts.Add($"rentals {state.lastWeeklyPlayerOwnedRentalMoveCount}, boarding {state.lastWeeklyPlayerOwnedBoardingMoveCount}");
            }
            else if (state.lastWeeklyPlayerOwnedRentalMoveCount > 0)
            {
                parts.Add($"player-owned rental placements {state.lastWeeklyPlayerOwnedRentalMoveCount}");
            }
            else if (state.lastWeeklyPlayerOwnedBoardingMoveCount > 0)
            {
                parts.Add($"player-owned boarding placements {state.lastWeeklyPlayerOwnedBoardingMoveCount}");
            }

            if (state.rentalApplicantCount > 0)
            {
                parts.Add($"{state.rentalApplicantCount} applicant(s) still waiting");
            }

            if (state.transientPersonCount > 0)
            {
                parts.Add($"{state.transientPersonCount} transient resident(s) remain");
            }

            return string.Join("; ", parts) + ".";
        }

        private static void EnsureBoardingHouseHouseholds(
            PopulationState state,
            IReadOnlyList<NewcomerSettlementBoardingOption> boardingHouseOptions)
        {
            if (state == null || state.households == null || boardingHouseOptions == null)
            {
                return;
            }

            for (int i = 0; i < boardingHouseOptions.Count; i++)
            {
                NewcomerSettlementBoardingOption option = boardingHouseOptions[i];
                if (option.BuildingId < 0)
                {
                    continue;
                }

                HouseholdState household = FindBoardingHouseGuestHousehold(
                    state,
                    option.BusinessInstanceId,
                    option.BuildingId);
                if (household == null)
                {
                    if (option.RoomCapacity <= 0)
                    {
                        continue;
                    }

                    household = new HouseholdState
                    {
                        id = GetNextHouseholdId(state),
                        householdName = $"{option.DisplayName} Guests",
                        surname = "Boarding",
                        homeBuildingId = option.BuildingId,
                        spendingMoneyCents = 0,
                        weeklyIncomeSnapshot = 0,
                        arrivalProfile = NewcomerArrivalProfile.BoarderSeekingWorker,
                        settlementArrangement = SettlementArrangement.Boarding,
                        dwellingKind = HouseholdDwellingKind.BoardingHouseLodging,
                        dwellingSummary = "Boarding-house guest rooms",
                        demandSnapshot = HouseholdDemandSnapshot.Empty(),
                        baseBoardingCapacity = option.RoomCapacity,
                        isBoardingHouseGuestHousehold = true,
                        isBoardingHouseLodging = true,
                        lodgingBusinessInstanceId = option.BusinessInstanceId,
                        boardingBusinessInstanceId = option.BusinessInstanceId,
                        lastSettlementSummary = $"{option.DisplayName} has open boarding rooms"
                    };
                    state.households.Add(household);
                }

                household.isBoardingHouseGuestHousehold = true;
                household.isBoardingHouseLodging = true;
                household.lodgingBusinessInstanceId = option.BusinessInstanceId;
                household.boardingBusinessInstanceId = option.BusinessInstanceId;
                household.homeBuildingId = option.BuildingId;
                household.settlementArrangement = SettlementArrangement.Boarding;
                household.dwellingKind = HouseholdDwellingKind.BoardingHouseLodging;
                household.dwellingSummary = "Boarding-house guest rooms";
                household.baseBoardingCapacity = Mathf.Max(0, option.RoomCapacity);
                household.boarderPersonIds ??= new List<int>();
                household.memberIds ??= new List<int>();
                for (int boarderIndex = 0; boarderIndex < household.boarderPersonIds.Count; boarderIndex++)
                {
                    int personId = household.boarderPersonIds[boarderIndex];
                    if (!household.memberIds.Contains(personId))
                    {
                        household.memberIds.Add(personId);
                    }
                }

                NewcomerSettlementEvaluator.RefreshHouseholdPressures(household);
            }
        }

        private static int ResolveRentalApplications(
            PopulationState state,
            PopulationGenerationSettings settings,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions)
        {
            if (state == null || state.people == null || state.households == null)
            {
                return 0;
            }

            Dictionary<int, int> previousWaitWeeks = BuildApplicantWaitWeeks(state);
            List<RentalApplicantState> applicants = BuildRentalApplicants(state, settings, previousWaitWeeks);
            List<NewcomerSettlementHomeOption> vacancies = BuildVacantRentalHomes(state, homeOptions);
            SortRentalApplicants(applicants);

            state.rentalApplicants.Clear();
            int moved = 0;
            int applicantIndex = 0;
            int vacancyIndex = 0;
            while (vacancyIndex < vacancies.Count && applicantIndex < applicants.Count)
            {
                RentalApplicantState applicant = applicants[applicantIndex];
                NewcomerSettlementHomeOption vacancy = vacancies[vacancyIndex];
                int movedPeople = TryPlaceRentalApplicant(state, applicant, vacancy);
                if (movedPeople <= 0)
                {
                    applicant.acceptedLastWeek = false;
                    applicant.status = BuildUnableRentalStatus(applicant);
                    state.rentalApplicants.Add(applicant);
                    applicantIndex++;
                    continue;
                }

                applicant.acceptedLastWeek = true;
                applicant.preferredBuildingId = vacancy.BuildingId;
                applicant.currentArrangement = SettlementArrangement.Renting;
                applicant.status = $"Accepted at {vacancy.DisplayName}{(vacancy.PlayerOwned ? " (player-owned)" : string.Empty)}; moved {movedPeople} resident(s).";
                state.rentalApplicants.Add(applicant);
                if (vacancy.PlayerOwned)
                {
                    state.lastWeeklyPlayerOwnedRentalMoveCount += movedPeople;
                }

                moved += movedPeople;
                applicantIndex++;
                vacancyIndex++;
            }

            for (; applicantIndex < applicants.Count; applicantIndex++)
            {
                RentalApplicantState applicant = applicants[applicantIndex];
                applicant.acceptedLastWeek = false;
                applicant.status = BuildWaitingRentalStatus(applicant, vacancies.Count, vacancyIndex);
                state.rentalApplicants.Add(applicant);
            }

            return moved;
        }

        private static int TryPlaceRentalApplicant(PopulationState state, RentalApplicantState applicant, NewcomerSettlementHomeOption vacancy)
        {
            if (state == null || applicant == null || vacancy.BuildingId < 0)
            {
                return 0;
            }

            HouseholdState householdApplicant = GetHousehold(state, applicant.householdId);
            if (householdApplicant != null
                && householdApplicant.MemberCount > 1
                && (householdApplicant.settlementArrangement == SettlementArrangement.Kin
                    || householdApplicant.settlementArrangement == SettlementArrangement.Transient
                    || householdApplicant.settlementArrangement == SettlementArrangement.Boarding))
            {
                int peopleMoved = householdApplicant.MemberCount;
                return TryMoveHouseholdToVacantHome(
                    state,
                    householdApplicant,
                    vacancy,
                    SettlementArrangement.Renting,
                    true,
                    "Household accepted rented rooms")
                    ? peopleMoved
                    : 0;
            }

            PersonState person = state.GetPerson(applicant.personId);
            if (person == null)
            {
                return 0;
            }

            return TryMoveSinglePersonToVacantHome(
                state,
                person,
                vacancy,
                SettlementArrangement.Renting,
                true,
                "Accepted for rented rooms")
                ? 1
                : 0;
        }

        private static string BuildUnableRentalStatus(RentalApplicantState applicant)
        {
            string name = applicant != null && !string.IsNullOrWhiteSpace(applicant.displayName) ? applicant.displayName : "Applicant";
            return $"Unable this week: {name} no longer matched a movable household/person record.";
        }

        private static string BuildWaitingRentalStatus(RentalApplicantState applicant, int vacancyCount, int vacancyIndex)
        {
            string details = applicant == null
                ? string.Empty
                : $" wait {Mathf.Max(0, applicant.weeksWaiting)}w, cash ${applicant.savingsCents / 100f:0.00}, income ${applicant.weeklyIncomeCents / 100f:0.00}, score {applicant.score01:P0}";
            if (vacancyCount <= 0)
            {
                return "Waiting for a rentable vacancy." + details;
            }

            return vacancyIndex >= vacancyCount
                ? "Waiting because this week's open rooms went to stronger or earlier applicants." + details
                : "Waiting for settlement records to refresh." + details;
        }

        private static Dictionary<int, int> BuildApplicantWaitWeeks(PopulationState state)
        {
            Dictionary<int, int> result = new();
            if (state == null || state.rentalApplicants == null)
            {
                return result;
            }

            for (int i = 0; i < state.rentalApplicants.Count; i++)
            {
                RentalApplicantState applicant = state.rentalApplicants[i];
                if (applicant != null && applicant.personId >= 0)
                {
                    result[applicant.personId] = Mathf.Max(0, applicant.weeksWaiting);
                }
            }

            return result;
        }

        private static List<RentalApplicantState> BuildRentalApplicants(
            PopulationState state,
            PopulationGenerationSettings settings,
            Dictionary<int, int> previousWaitWeeks)
        {
            List<RentalApplicantState> applicants = new();
            if (state == null || state.people == null)
            {
                return applicants;
            }

            HashSet<int> representedHouseholds = new();
            if (state.households != null)
            {
                for (int householdIndex = 0; householdIndex < state.households.Count; householdIndex++)
                {
                    HouseholdState household = state.households[householdIndex];
                    if (!IsHouseholdRentalApplicantEligible(state, household, settings, out PersonState representative))
                    {
                        continue;
                    }

                    int waitWeeks = representative != null && previousWaitWeeks != null && previousWaitWeeks.TryGetValue(representative.id, out int prior)
                        ? prior + 1
                        : Mathf.Max(1, household.settlementPressure.weeksInArrangement);
                    RentalApplicantState applicant = new()
                    {
                        personId = representative != null ? representative.id : -1,
                        householdId = household.id,
                        displayName = string.IsNullOrWhiteSpace(household.householdName) ? $"{household.surname} Household" : household.householdName,
                        arrivalProfile = household.arrivalProfile,
                        currentArrangement = household.settlementArrangement,
                        savingsCents = Mathf.Max(household.settlementReserveStrengthCents, household.spendingMoneyCents),
                        weeklyIncomeCents = Mathf.Max(0, household.weeklyIncomeSnapshot * 100),
                        weeksWaiting = waitWeeks,
                        score01 = BuildHouseholdRentalApplicantScore(household, representative, waitWeeks),
                        status = "Household ready to apply"
                    };
                    applicant.Sanitize();
                    applicants.Add(applicant);
                    representedHouseholds.Add(household.id);
                }
            }

            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                if (person != null && representedHouseholds.Contains(person.householdId))
                {
                    continue;
                }

                if (!IsRentalApplicantEligible(person, settings))
                {
                    continue;
                }

                int waitWeeks = previousWaitWeeks != null && previousWaitWeeks.TryGetValue(person.id, out int prior)
                    ? prior + 1
                    : 1;
                RentalApplicantState applicant = new()
                {
                    personId = person.id,
                    householdId = person.householdId,
                    displayName = person.DisplayName,
                    arrivalProfile = person.arrivalProfile,
                    currentArrangement = person.settlementArrangement,
                    savingsCents = Mathf.Max(0, person.startingCashCents),
                    weeklyIncomeCents = Mathf.Max(0, person.wage.weeklyWage * 100),
                    weeksWaiting = waitWeeks,
                    score01 = BuildRentalApplicantScore(person, waitWeeks),
                    status = "Ready to apply"
                };
                applicant.Sanitize();
                applicants.Add(applicant);
            }

            return applicants;
        }

        private static bool IsHouseholdRentalApplicantEligible(
            PopulationState state,
            HouseholdState household,
            PopulationGenerationSettings settings,
            out PersonState representative)
        {
            representative = null;
            if (state == null
                || household == null
                || household.id < 0
                || household.memberIds == null
                || household.memberIds.Count <= 1
                || household.isRenterHousehold
                || household.isBoardingHouseGuestHousehold
                || household.settlementArrangement == SettlementArrangement.StableHousehold
                || household.settlementArrangement == SettlementArrangement.Renting
                || household.settlementArrangement == SettlementArrangement.Departed)
            {
                return false;
            }

            household.EnsureSettlementStateInitialized();
            int minimumWeeks = settings != null ? Mathf.Max(1, settings.boarderStableMoveMinWeeks) : 4;
            int requiredWeeks = household.settlementArrangement == SettlementArrangement.Transient
                ? Mathf.Max(1, minimumWeeks - 1)
                : minimumWeeks;
            if (household.settlementPressure.weeksInArrangement < requiredWeeks)
            {
                return false;
            }

            representative = FindBestRentalRepresentative(state, household);
            if (representative == null)
            {
                return false;
            }

            int effectiveSavings = Mathf.Max(household.settlementReserveStrengthCents, household.spendingMoneyCents)
                + Mathf.Max(0, household.weeklyIncomeSnapshot * 100);
            int savingsThreshold = Mathf.Max(2600, GetRentalSavingsThreshold(household.arrivalProfile) + (household.MemberCount - 1) * 500);
            return effectiveSavings >= savingsThreshold
                && household.settlementPressure.housingFriction01 <= 0.82f
                && NewcomerSettlementEvaluator.GetLaborReadiness01(representative) >= 0.42f;
        }

        private static bool IsRentalApplicantEligible(PersonState person, PopulationGenerationSettings settings)
        {
            if (person == null
                || person.settlementArrangement == SettlementArrangement.Renting
                || person.settlementArrangement == SettlementArrangement.StableHousehold
                || person.settlementArrangement == SettlementArrangement.Departed
                || person.laborAccessLevel < LaborAccessLevel.YoungWorker)
            {
                return false;
            }

            NewcomerSettlementEvaluator.EnsurePersonSettlementInitialized(person);
            int minimumWeeks = settings != null ? Mathf.Max(1, settings.boarderStableMoveMinWeeks) : 4;
            if (person.settlementArrangement == SettlementArrangement.Boarding
                && person.settlementPressure.weeksInArrangement < minimumWeeks)
            {
                return false;
            }

            if (person.settlementArrangement == SettlementArrangement.Kin
                && person.settlementPressure.weeksInArrangement < Mathf.Max(2, minimumWeeks - 1))
            {
                return false;
            }

            int savingsThreshold = GetRentalSavingsThreshold(person.arrivalProfile);
            int effectiveSavings = Mathf.Max(0, person.startingCashCents) + Mathf.Max(0, person.wage.weeklyWage * 100);
            return effectiveSavings >= savingsThreshold
                && NewcomerSettlementEvaluator.GetLaborReadiness01(person) >= 0.45f;
        }

        private static int GetRentalSavingsThreshold(NewcomerArrivalProfile profile)
        {
            return profile switch
            {
                NewcomerArrivalProfile.RenterReadyHousehold => 4500,
                NewcomerArrivalProfile.SkilledCapitalizedArrival => 5200,
                NewcomerArrivalProfile.BoarderSeekingWorker => 2800,
                NewcomerArrivalProfile.LoneLaborer => 3200,
                NewcomerArrivalProfile.DistressedRelocationHousehold => 2200,
                NewcomerArrivalProfile.WidowElderDependentRelocation => 2600,
                _ => 3000
            };
        }

        private static float BuildRentalApplicantScore(PersonState person, int weeksWaiting)
        {
            if (person == null)
            {
                return 0f;
            }

            float savings = Mathf.Clamp01(person.startingCashCents / 12000f);
            float wage = Mathf.Clamp01((person.wage.weeklyWage * 100) / 5000f);
            float readiness = NewcomerSettlementEvaluator.GetLaborReadiness01(person);
            float stability = person.settlementPressure.initialized ? person.settlementPressure.stability01 : 0.45f;
            float profileBonus = person.arrivalProfile switch
            {
                NewcomerArrivalProfile.RenterReadyHousehold => 0.18f,
                NewcomerArrivalProfile.SkilledCapitalizedArrival => 0.16f,
                NewcomerArrivalProfile.BoarderSeekingWorker => 0.08f,
                NewcomerArrivalProfile.LoneLaborer => 0.04f,
                _ => 0f
            };
            float waiting = Mathf.Clamp01(weeksWaiting / 8f) * 0.08f;
            return Mathf.Clamp01(savings * 0.32f + wage * 0.20f + readiness * 0.24f + stability * 0.18f + profileBonus + waiting);
        }

        private static float BuildHouseholdRentalApplicantScore(HouseholdState household, PersonState representative, int weeksWaiting)
        {
            if (household == null)
            {
                return 0f;
            }

            float reserves = Mathf.Clamp01(Mathf.Max(household.settlementReserveStrengthCents, household.spendingMoneyCents) / 16000f);
            float income = Mathf.Clamp01((household.weeklyIncomeSnapshot * 100) / 6500f);
            float readiness = representative != null ? NewcomerSettlementEvaluator.GetLaborReadiness01(representative) : 0.35f;
            float stability = household.settlementPressure.initialized ? household.settlementPressure.stability01 : 0.45f;
            float waiting = Mathf.Clamp01(weeksWaiting / 8f) * 0.10f;
            float sizePenalty = Mathf.Clamp01((household.MemberCount - 1) / 5f) * 0.08f;
            float profileBonus = household.arrivalProfile switch
            {
                NewcomerArrivalProfile.RenterReadyHousehold => 0.14f,
                NewcomerArrivalProfile.KinLinkedArrival => 0.10f,
                NewcomerArrivalProfile.DistressedRelocationHousehold => 0.06f,
                _ => 0f
            };

            return Mathf.Clamp01(reserves * 0.34f + income * 0.22f + readiness * 0.20f + stability * 0.16f + waiting + profileBonus - sizePenalty);
        }

        private static PersonState FindBestRentalRepresentative(PopulationState state, HouseholdState household)
        {
            if (state == null || household == null || household.memberIds == null)
            {
                return null;
            }

            PersonState best = null;
            float bestReadiness = float.MinValue;
            for (int i = 0; i < household.memberIds.Count; i++)
            {
                PersonState person = state.GetPerson(household.memberIds[i]);
                if (person == null || person.laborAccessLevel < LaborAccessLevel.YoungWorker)
                {
                    continue;
                }

                float readiness = NewcomerSettlementEvaluator.GetLaborReadiness01(person);
                if (best == null || readiness > bestReadiness)
                {
                    best = person;
                    bestReadiness = readiness;
                }
            }

            return best;
        }

        private static void SortRentalApplicants(List<RentalApplicantState> applicants)
        {
            applicants.Sort((left, right) =>
            {
                int score = right.score01.CompareTo(left.score01);
                if (score != 0)
                {
                    return score;
                }

                int savings = right.savingsCents.CompareTo(left.savingsCents);
                return savings != 0 ? savings : left.personId.CompareTo(right.personId);
            });
        }

        private static List<NewcomerSettlementHomeOption> BuildVacantRentalHomes(
            PopulationState state,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions)
        {
            List<NewcomerSettlementHomeOption> result = new();
            if (homeOptions == null)
            {
                return result;
            }

            Dictionary<int, int> occupied = BuildOccupiedHomeCounts(state);
            for (int i = 0; i < homeOptions.Count; i++)
            {
                NewcomerSettlementHomeOption option = homeOptions[i];
                if (!option.RentalCapable || option.ResidentHouseholdCapacity <= 0 || IsBoardingHouseGuestBuilding(state, option.BuildingId))
                {
                    continue;
                }

                int used = occupied.TryGetValue(option.BuildingId, out int count) ? count : 0;
                int vacancyCount = Mathf.Max(0, option.ResidentHouseholdCapacity - used);
                for (int vacancyIndex = 0; vacancyIndex < vacancyCount; vacancyIndex++)
                {
                    result.Add(option);
                }
            }

            result.Sort((left, right) =>
            {
                int ownership = right.PlayerOwned.CompareTo(left.PlayerOwned);
                return ownership != 0 ? ownership : left.BuildingId.CompareTo(right.BuildingId);
            });
            return result;
        }

        private static void AccrueSettlementSavings(PersonState person)
        {
            if (person == null
                || person.settlementArrangement == SettlementArrangement.StableHousehold
                || person.settlementArrangement == SettlementArrangement.Renting
                || person.settlementArrangement == SettlementArrangement.Departed)
            {
                return;
            }

            int weeklyWageCents = Mathf.Max(0, person.wage.weeklyWage * 100);
            int savings = Mathf.RoundToInt(weeklyWageCents * 0.22f);
            if (savings <= 0 && NewcomerSettlementEvaluator.GetLaborReadiness01(person) >= 0.55f)
            {
                savings = 50;
            }

            person.startingCashCents = Mathf.Max(0, person.startingCashCents + savings);
        }

        private static bool IsBoardingHostPlayerOwned(
            HouseholdState host,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions,
            IReadOnlyList<NewcomerSettlementBoardingOption> boardingHouseOptions)
        {
            if (host == null)
            {
                return false;
            }

            if (host.isBoardingHouseGuestHousehold && boardingHouseOptions != null)
            {
                for (int i = 0; i < boardingHouseOptions.Count; i++)
                {
                    NewcomerSettlementBoardingOption option = boardingHouseOptions[i];
                    bool businessMatch = !string.IsNullOrWhiteSpace(option.BusinessInstanceId)
                        && string.Equals(option.BusinessInstanceId, host.lodgingBusinessInstanceId, StringComparison.OrdinalIgnoreCase);
                    if ((businessMatch || option.BuildingId == host.homeBuildingId) && option.PlayerOwned)
                    {
                        return true;
                    }
                }
            }

            if (homeOptions != null)
            {
                for (int i = 0; i < homeOptions.Count; i++)
                {
                    NewcomerSettlementHomeOption option = homeOptions[i];
                    if (option.BuildingId == host.homeBuildingId)
                    {
                        return option.PlayerOwned;
                    }
                }
            }

            return false;
        }

        private static HouseholdState PickBoardingHost(
            PopulationState state,
            System.Random random,
            int excludedHouseholdId = -1,
            IReadOnlyList<NewcomerSettlementHomeOption> homeOptions = null,
            IReadOnlyList<NewcomerSettlementBoardingOption> boardingHouseOptions = null)
        {
            if (state == null || state.households == null)
            {
                return null;
            }

            List<HouseholdState> playerOwnedBusinessCandidates = new();
            List<HouseholdState> businessCandidates = new();
            List<HouseholdState> playerOwnedHouseholdCandidates = new();
            List<HouseholdState> householdCandidates = new();
            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household == null || household.id == excludedHouseholdId || household.settlementArrangement == SettlementArrangement.Transient)
                {
                    continue;
                }

                if (NewcomerSettlementEvaluator.GetOpenBoardingSlots(household) <= 0)
                {
                    continue;
                }

                bool playerOwned = IsBoardingHostPlayerOwned(household, homeOptions, boardingHouseOptions);
                if (household.isBoardingHouseGuestHousehold)
                {
                    if (playerOwned)
                    {
                        playerOwnedBusinessCandidates.Add(household);
                    }
                    else
                    {
                        businessCandidates.Add(household);
                    }
                }
                else if (playerOwned)
                {
                    playerOwnedHouseholdCandidates.Add(household);
                }
                else
                {
                    householdCandidates.Add(household);
                }
            }

            if (playerOwnedBusinessCandidates.Count > 0)
            {
                return playerOwnedBusinessCandidates[random.Next(0, playerOwnedBusinessCandidates.Count)];
            }

            if (businessCandidates.Count > 0)
            {
                return businessCandidates[random.Next(0, businessCandidates.Count)];
            }

            if (playerOwnedHouseholdCandidates.Count > 0)
            {
                return playerOwnedHouseholdCandidates[random.Next(0, playerOwnedHouseholdCandidates.Count)];
            }

            return householdCandidates.Count > 0 ? householdCandidates[random.Next(0, householdCandidates.Count)] : null;
        }

        private static HouseholdState PickKinHost(PopulationState state, System.Random random)
        {
            if (state == null || state.households == null || state.households.Count == 0)
            {
                return null;
            }

            List<HouseholdState> candidates = new();
            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household == null
                    || household.settlementArrangement == SettlementArrangement.Transient
                    || household.memberIds == null
                    || household.memberIds.Count <= 0
                    || household.memberIds.Count >= 8)
                {
                    continue;
                }

                candidates.Add(household);
            }

            return candidates.Count > 0 ? candidates[random.Next(0, candidates.Count)] : null;
        }

        private static bool TryFindVacantHome(PopulationState state, IReadOnlyList<NewcomerSettlementHomeOption> homeOptions, out NewcomerSettlementHomeOption home)
        {
            home = default;
            if (homeOptions == null || homeOptions.Count == 0)
            {
                return false;
            }

            Dictionary<int, int> occupied = BuildOccupiedHomeCounts(state);
            for (int i = 0; i < homeOptions.Count; i++)
            {
                NewcomerSettlementHomeOption option = homeOptions[i];
                if (!option.RentalCapable || IsBoardingHouseGuestBuilding(state, option.BuildingId))
                {
                    continue;
                }

                int used = occupied.TryGetValue(option.BuildingId, out int count) ? count : 0;
                if (used < option.ResidentHouseholdCapacity)
                {
                    home = option;
                    return true;
                }
            }

            return false;
        }

        private static HouseholdState FindBoardingHouseGuestHousehold(PopulationState state, string businessInstanceId, int buildingId)
        {
            if (state == null || state.households == null)
            {
                return null;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                household?.EnsureSettlementStateInitialized();
                if (household == null || !household.isBoardingHouseGuestHousehold)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(businessInstanceId)
                    && string.Equals(household.lodgingBusinessInstanceId, businessInstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return household;
                }

                if (household.homeBuildingId == buildingId)
                {
                    return household;
                }
            }

            return null;
        }

        private static bool IsBoardingHouseGuestBuilding(PopulationState state, int buildingId)
        {
            if (state == null || state.households == null || buildingId < 0)
            {
                return false;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                household?.EnsureSettlementStateInitialized();
                if (household != null
                    && household.isBoardingHouseGuestHousehold
                    && household.homeBuildingId == buildingId)
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<int, int> BuildOccupiedHomeCounts(PopulationState state)
        {
            Dictionary<int, int> result = new();
            if (state == null || state.households == null)
            {
                return result;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                household?.EnsureSettlementStateInitialized();
                if (household == null
                    || household.homeBuildingId < 0
                    || household.settlementArrangement == SettlementArrangement.Transient
                    || household.isBoardingHouseGuestHousehold)
                {
                    continue;
                }

                result.TryGetValue(household.homeBuildingId, out int count);
                result[household.homeBuildingId] = count + 1;
            }

            return result;
        }

        private static int PickFallbackHomeBuildingId(PopulationState state, IReadOnlyList<NewcomerSettlementHomeOption> homeOptions)
        {
            if (state != null && state.households != null && state.households.Count > 0)
            {
                for (int i = 0; i < state.households.Count; i++)
                {
                    HouseholdState household = state.households[i];
                    household?.EnsureSettlementStateInitialized();
                    if (household != null
                        && household.homeBuildingId >= 0
                        && !household.isBoardingHouseGuestHousehold)
                    {
                        return household.homeBuildingId;
                    }
                }
            }

            return homeOptions != null && homeOptions.Count > 0 ? homeOptions[0].BuildingId : -1;
        }

        private static string PickBoarderSurname(PopulationGenerationSettings settings, HouseholdState host, NewcomerProfileDefinition profile, System.Random random)
        {
            if (profile.Profile == NewcomerArrivalProfile.KinLinkedArrival
                || profile.Profile == NewcomerArrivalProfile.WidowElderDependentRelocation)
            {
                return host != null && !string.IsNullOrWhiteSpace(host.surname)
                    ? host.surname
                    : PopulationGenerator.Pick(settings.surnames, random, "Hale");
            }

            return PopulationGenerator.Pick(settings.surnames, random, "Hale");
        }

        private static int CountHomeCapacity(IReadOnlyList<NewcomerSettlementHomeOption> homeOptions)
        {
            int capacity = 0;
            if (homeOptions == null)
            {
                return 0;
            }

            for (int i = 0; i < homeOptions.Count; i++)
            {
                capacity += Mathf.Max(0, homeOptions[i].ResidentHouseholdCapacity);
            }

            return capacity;
        }

        private static int CountDistinctActiveHomeBuildings(PopulationState state)
        {
            List<int> ids = new();
            if (state == null || state.households == null)
            {
                return 0;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household == null || household.homeBuildingId < 0)
                {
                    continue;
                }

                if (!ids.Contains(household.homeBuildingId))
                {
                    ids.Add(household.homeBuildingId);
                }
            }

            return ids.Count;
        }

        private static int CountWaitingRentalApplicants(PopulationState state)
        {
            if (state == null || state.rentalApplicants == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < state.rentalApplicants.Count; i++)
            {
                RentalApplicantState applicant = state.rentalApplicants[i];
                if (applicant != null
                    && !applicant.acceptedLastWeek
                    && (string.IsNullOrWhiteSpace(applicant.status)
                        || !applicant.status.StartsWith("Unable this week", StringComparison.OrdinalIgnoreCase)))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountStrongerHousingNeed(PopulationState state)
        {
            if (state == null || state.people == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                if (person == null
                    || person.settlementArrangement == SettlementArrangement.Renting
                    || person.settlementArrangement == SettlementArrangement.StableHousehold
                    || person.settlementArrangement == SettlementArrangement.Departed)
                {
                    continue;
                }

                NewcomerSettlementEvaluator.EnsurePersonSettlementInitialized(person);
                if (person.settlementArrangement == SettlementArrangement.Transient
                    || person.settlementArrangement == SettlementArrangement.Boarding
                    || person.settlementPressure.homeAmbition01 >= 0.48f
                    || person.settlementPressure.housingFriction01 >= 0.38f)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool FindHomeOption(IReadOnlyList<NewcomerSettlementHomeOption> homeOptions, int buildingId, out NewcomerSettlementHomeOption option)
        {
            if (homeOptions != null)
            {
                for (int i = 0; i < homeOptions.Count; i++)
                {
                    if (homeOptions[i].BuildingId == buildingId)
                    {
                        option = homeOptions[i];
                        return true;
                    }
                }
            }

            option = default;
            return false;
        }

        private static HouseholdState GetHousehold(PopulationState state, int householdId)
        {
            if (state == null || state.households == null)
            {
                return null;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                if (state.households[i] != null && state.households[i].id == householdId)
                {
                    return state.households[i];
                }
            }

            return null;
        }

        private static void RemoveEmptyTemporaryHousehold(PopulationState state, HouseholdState household)
        {
            if (state == null || state.households == null || household == null || household.isBoardingHouseGuestHousehold)
            {
                return;
            }

            if (household.memberIds != null && household.memberIds.Count > 0)
            {
                return;
            }

            if (household.boarderPersonIds != null && household.boarderPersonIds.Count > 0)
            {
                return;
            }

            if (household.settlementArrangement == SettlementArrangement.Transient
                || household.settlementArrangement == SettlementArrangement.Boarding
                || household.settlementArrangement == SettlementArrangement.Kin
                || household.isTransientHousehold)
            {
                state.households.Remove(household);
            }
        }

        private static void RemovePersonFromHousehold(HouseholdState household, int personId)
        {
            if (household == null)
            {
                return;
            }

            household.memberIds?.Remove(personId);
            household.boarderPersonIds?.Remove(personId);
        }

        private static int GetNextHouseholdId(PopulationState state)
        {
            int next = 0;
            if (state == null || state.households == null)
            {
                return next;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                if (state.households[i] != null)
                {
                    next = Mathf.Max(next, state.households[i].id + 1);
                }
            }

            return next;
        }

        private static string BuildSettlementSummary(PopulationState state)
        {
            string weekly = string.IsNullOrWhiteSpace(state.lastWeeklySettlementActionSummary)
                ? string.Empty
                : $" Weekly: {state.lastWeeklySettlementActionSummary}";
            return $"Settlement: boarders {state.boardingUsed}/{state.boardingCapacity} (boarding house {state.boardingHouseUsed}/{state.boardingHouseCapacity}), renters {state.renterHouseholdCount}, kin {state.kinPlacementCount}, transients {state.transientPersonCount}, vacancies {state.rentalVacancyCount} ({state.playerOwnedRentalVacancyCount} player-owned), applicants {state.rentalApplicantCount}, housing pressure {state.housingPressure01:P0}, labor absorption {state.laborAbsorption01:P0}.{weekly}";
        }

        private static string BuildRentalSummary(PopulationState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            string pressure = state.strongerHousingNeedCount > state.rentalVacancyCount
                ? "more housing needed"
                : state.rentalVacancyCount > 0 ? "vacancies available" : "no open rentable rooms";

            List<string> parts = new()
            {
                $"Renting: vacancies {state.rentalVacancyCount} ({state.playerOwnedRentalVacancyCount} player-owned)",
                $"applicants {state.rentalApplicantCount}",
                $"stronger housing need {state.strongerHousingNeedCount}",
                pressure
            };

            int playerOwnedMoves = Mathf.Max(0, state.lastWeeklyPlayerOwnedRentalMoveCount + state.lastWeeklyPlayerOwnedBoardingMoveCount);
            if (playerOwnedMoves > 0)
            {
                parts.Add($"player-owned lodging absorbed {playerOwnedMoves} resident(s) this week");
            }
            else if (state.playerOwnedRentalVacancyCount > 0 && state.rentalApplicantCount <= 0)
            {
                parts.Add("player-owned rooms are open for future settlement demand");
            }
            else if (state.playerOwnedRentalVacancyCount > 0 && state.rentalApplicantCount > 0)
            {
                parts.Add("player-owned rooms can absorb waiting applicants");
            }

            string applicantStatus = BuildRentalApplicantStatusSummary(state);
            if (!string.IsNullOrWhiteSpace(applicantStatus))
            {
                parts.Add(applicantStatus);
            }

            return string.Join("; ", parts) + ".";
        }

        private static string BuildRentalApplicantStatusSummary(PopulationState state)
        {
            if (state?.rentalApplicants == null || state.rentalApplicants.Count <= 0)
            {
                return string.Empty;
            }

            int accepted = 0;
            int waiting = 0;
            int unable = 0;
            string firstWaiting = string.Empty;
            for (int i = 0; i < state.rentalApplicants.Count; i++)
            {
                RentalApplicantState applicant = state.rentalApplicants[i];
                if (applicant == null)
                {
                    continue;
                }

                if (applicant.acceptedLastWeek)
                {
                    accepted++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(applicant.status)
                    && applicant.status.StartsWith("Unable this week", StringComparison.OrdinalIgnoreCase))
                {
                    unable++;
                    continue;
                }

                waiting++;
                if (string.IsNullOrWhiteSpace(firstWaiting))
                {
                    firstWaiting = applicant.status;
                }
            }

            List<string> parts = new();
            if (accepted > 0)
            {
                parts.Add($"accepted {accepted}");
            }
            if (waiting > 0)
            {
                parts.Add($"waiting {waiting}");
            }
            if (unable > 0)
            {
                parts.Add($"stale this week {unable}");
            }
            if (!string.IsNullOrWhiteSpace(firstWaiting))
            {
                parts.Add($"first wait: {firstWaiting}");
            }

            return parts.Count > 0 ? "applicants " + string.Join(", ", parts) : string.Empty;
        }
    }
}
