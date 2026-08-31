using System.Text;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Time;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Population
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class PopulationManager : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField]
        private TownWorldController townWorld;

        [SerializeField]
        private PopulationGenerationSettings settings;

        [SerializeField]
        private TimeManager timeManager;

        [SerializeField]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        [Header("Generation")]
        [SerializeField]
        private bool generateOnStart;

        [SerializeField]
        private bool logSummary = true;

        [SerializeField]
        private bool logHouseholdPreview = true;

        [SerializeField, Min(1)]
        private int previewHouseholdLimit = 5;

        [Header("Runtime State")]
        [SerializeField]
        private PopulationState state = new();

        [SerializeField]
        private int generatedHouseholdCount;

        [SerializeField]
        private int generatedPersonCount;

        [SerializeField]
        private int assignedWorkerCount;

        [SerializeField]
        private int totalWeeklyWages;

        [SerializeField]
        private int lastHealthResolutionDayIndex = -1;

        [SerializeField]
        private int lastSettlementResolutionWeekIndex = -1;

        [SerializeField, TextArea(2, 6)]
        private string lastHealthSummary = "No health resolution yet.";

        [SerializeField, TextArea(2, 6)]
        private string lastSettlementSummary = string.Empty;

        [SerializeField, TextArea(2, 8)]
        private string lastGenerationSummary;

        private bool subscribedToTime;

        public PopulationState State => state;
        public int GeneratedHouseholdCount => generatedHouseholdCount;
        public int GeneratedPersonCount => generatedPersonCount;
        public int AssignedWorkerCount => assignedWorkerCount;
        public int TotalWeeklyWages => totalWeeklyWages;
        public int LastHealthResolutionDayIndex => lastHealthResolutionDayIndex;
        public string LastHealthSummary => lastHealthSummary;
        public int BoardingCapacity => state != null ? state.boardingCapacity : 0;
        public int BoardingUsed => state != null ? state.boardingUsed : 0;
        public int BoardingHouseCapacity => state != null ? state.boardingHouseCapacity : 0;
        public int BoardingHouseUsed => state != null ? state.boardingHouseUsed : 0;
        public int RentalVacancyCount => state != null ? state.rentalVacancyCount : 0;
        public int RentalApplicantCount => state != null ? state.rentalApplicantCount : 0;
        public int StrongerHousingNeedCount => state != null ? state.strongerHousingNeedCount : 0;
        public int TransientPersonCount => state != null ? state.transientPersonCount : 0;
        public float HousingPressure01 => state != null ? state.housingPressure01 : 0f;
        public float RentalPressure01 => state != null ? state.rentalPressure01 : 0f;
        public float LaborAbsorption01 => state != null ? state.laborAbsorption01 : 1f;
        public SettlementPressureSnapshot CurrentSettlementPressure => state != null ? state.settlementPressureSnapshot : SettlementPressureSnapshot.Empty();
        public PopulationValidationSnapshot CurrentPopulationValidation => state != null ? state.populationValidationSnapshot : PopulationValidationSnapshot.Clean();
        public TownReservePressureSnapshot CurrentTownReservePressure => state != null ? state.townReservePressureSnapshot : TownReservePressureSnapshot.Empty();
        public string LastTownReserveSummary => state != null ? state.lastTownReserveSummary : string.Empty;
        public int LastWeeklySettlementMoveCount => state != null ? state.lastWeeklySettlementMoveCount : 0;
        public int LastWeeklyPlayerOwnedRentalMoveCount => state != null ? state.lastWeeklyPlayerOwnedRentalMoveCount : 0;
        public int LastWeeklyPlayerOwnedBoardingMoveCount => state != null ? state.lastWeeklyPlayerOwnedBoardingMoveCount : 0;
        public string LastWeeklySettlementActionSummary => state != null ? state.lastWeeklySettlementActionSummary : string.Empty;
        public string LastSettlementSummary => lastSettlementSummary;
        public string LastRentalSummary => state != null ? state.lastRentalSummary : string.Empty;
        public string LastGenerationSummary => lastGenerationSummary;

        public HouseholdState GetHouseholdByHomeBuildingId(int homeBuildingId)
        {
            if (state == null || state.households == null)
            {
                return null;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household != null && household.homeBuildingId == homeBuildingId)
                {
                    household.EnsureHouseholdUpgradesInitialized();
                    HouseholdUpgradeCatalog.ClampReserveToCapacity(household);
                    return household;
                }
            }

            return null;
        }

        public void Configure(TownWorldController newTownWorld, PopulationGenerationSettings newSettings, bool shouldGenerateOnStart)
        {
            townWorld = newTownWorld;
            settings = newSettings;
            generateOnStart = shouldGenerateOnStart;
        }

        [ContextMenu("Generate Population Snapshot")]
        public void GeneratePopulationSnapshot()
        {
            AutoWireReferences();

            sharedBusinessRuntime?.InitializeIfNeeded();
            PopulationGenerationResult result = PopulationGenerator.Generate(
                townWorld,
                settings,
                BuildBoardingHouseSettlementOptions());
            state = result.State;
            InitializeHealthDefaults();
            RefreshSettlementMetrics(result.Summary);
            lastHealthResolutionDayIndex = -1;
            lastSettlementResolutionWeekIndex = -1;
            lastHealthSummary = "No health resolution yet.";
            RefreshSummaryFields(result.Summary);

            if (!logSummary)
            {
                return;
            }

            if (result.HasErrors)
            {
                Debug.LogWarning(result.Summary + "\n" + BuildValidationLog(), this);
            }
            else
            {
                string householdPreview = logHouseholdPreview ? "\n" + BuildHouseholdPreview() : string.Empty;
                string validationLog = state.validationMessages.Count > 0 ? "\n" + BuildValidationLog() : string.Empty;
                Debug.Log(result.Summary + validationLog + householdPreview, this);
            }
        }

        [ContextMenu("Clear Population Snapshot")]
        public void ClearPopulationSnapshot()
        {
            state.Clear();
            lastHealthResolutionDayIndex = -1;
            lastSettlementResolutionWeekIndex = -1;
            lastHealthSummary = "No health resolution yet.";
            RefreshSummaryFields("Population snapshot cleared.");
        }

        public void RecalculateHouseholdIncomeAndSummaries(string summary = null)
        {
            RecalculateHouseholdIncomeSnapshots();
            RefreshSettlementMetrics(summary ?? "Population employment updated.");
            RefreshSummaryFields(summary ?? "Population employment updated.");
        }

        public void RefreshSettlementMetrics(string summary = null)
        {
            if (state == null)
            {
                lastSettlementSummary = string.Empty;
                return;
            }

            NewcomerSettlementPlanner.RefreshSettlementMetrics(
                state,
                NewcomerSettlementPlanner.BuildHomeOptions(townWorld, settings),
                BuildBoardingHouseSettlementOptions());
            RefreshTownReservePressure();
            lastSettlementSummary = string.IsNullOrWhiteSpace(summary)
                ? state.lastSettlementSummary
                : $"{summary} {state.lastSettlementSummary}";
        }

        public IReadOnlyList<NewcomerSettlementBoardingOption> BuildBoardingHouseSettlementOptions()
        {
            AutoWireReferences();
            return sharedBusinessRuntime != null
                ? sharedBusinessRuntime.BuildBoardingHouseSettlementOptions()
                : System.Array.Empty<NewcomerSettlementBoardingOption>();
        }

        public int ResolveWeeklySettlementProgression()
        {
            AutoWireReferences();
            int moved = NewcomerSettlementPlanner.ResolveWeeklySettlementProgression(
                state,
                settings,
                NewcomerSettlementPlanner.BuildHomeOptions(townWorld, settings),
                new System.Random(BuildWeeklySettlementSeed()),
                BuildBoardingHouseSettlementOptions());
            string progressionSummary = state != null && !string.IsNullOrWhiteSpace(state.lastWeeklySettlementActionSummary)
                ? state.lastWeeklySettlementActionSummary
                : moved > 0
                    ? $"Settlement progression moved {moved} resident(s)."
                    : "Settlement progression checked.";
            RecalculateHouseholdIncomeAndSummaries(progressionSummary);
            return moved;
        }

        public string BuildTownSettlementHeadline()
        {
            if (state == null)
            {
                return "Settlement records unavailable.";
            }

            RefreshSettlementMetrics();
            state.EnsureSettlementMetricsInitialized();
            SettlementPressureSnapshot pressure = state.settlementPressureSnapshot;

            StringBuilder builder = new();
            string headline = string.IsNullOrWhiteSpace(pressure.headline) ? "Town settlement" : pressure.headline;
            builder.Append(headline);
            builder.Append(" | ");
            builder.Append(pressure.pressureBand);
            builder.Append(' ');
            builder.Append(pressure.pressureScore);
            builder.Append('%');
            builder.Append(" | Boarding ");
            builder.Append(state.boardingUsed);
            builder.Append('/');
            builder.Append(state.boardingCapacity);
            builder.Append(" | Vacancies ");
            builder.Append(state.rentalVacancyCount);
            if (state.playerOwnedRentalVacancyCount > 0)
            {
                builder.Append(" (");
                builder.Append(state.playerOwnedRentalVacancyCount);
                builder.Append(" player-owned)");
            }

            builder.Append(" | Applicants ");
            builder.Append(state.rentalApplicantCount);

            if (state.strongerHousingNeedCount > 0)
            {
                builder.Append(" | Housing seekers ");
                builder.Append(state.strongerHousingNeedCount);
            }

            if (state.transientPersonCount > 0)
            {
                builder.Append(" | Transients ");
                builder.Append(state.transientPersonCount);
            }

            int playerOwnedMoves = state.lastWeeklyPlayerOwnedRentalMoveCount + state.lastWeeklyPlayerOwnedBoardingMoveCount;
            if (playerOwnedMoves > 0)
            {
                builder.Append(" | Player lodging absorbed ");
                builder.Append(playerOwnedMoves);
                if (state.lastWeeklyPlayerOwnedRentalMoveCount > 0 && state.lastWeeklyPlayerOwnedBoardingMoveCount > 0)
                {
                    builder.Append(" (rentals ");
                    builder.Append(state.lastWeeklyPlayerOwnedRentalMoveCount);
                    builder.Append(", boarding ");
                    builder.Append(state.lastWeeklyPlayerOwnedBoardingMoveCount);
                    builder.Append(')');
                }
            }

            if (!string.IsNullOrWhiteSpace(pressure.recommendedAction)
                && pressure.pressureBand != SettlementPressureBand.Calm)
            {
                builder.Append(" | Action: ");
                builder.Append(pressure.recommendedAction);
            }

            if (!string.IsNullOrWhiteSpace(state.lastWeeklySettlementActionSummary))
            {
                builder.Append(" | Weekly: ");
                builder.Append(state.lastWeeklySettlementActionSummary);
            }

            if (state.populationValidationSnapshot.severity == PopulationValidationSeverity.Error
                || state.populationValidationSnapshot.severity == PopulationValidationSeverity.Warning)
            {
                builder.Append(" | Population records: ");
                builder.Append(state.populationValidationSnapshot.headline);
            }

            TownReservePressureSnapshot reservePressure = state.townReservePressureSnapshot;
            if (reservePressure.pressureBand != TownReservePressureBand.Stable
                && !string.IsNullOrWhiteSpace(reservePressure.headline))
            {
                builder.Append(" | Reserves: ");
                builder.Append(reservePressure.headline);
                if (!string.IsNullOrWhiteSpace(reservePressure.recommendedAction))
                {
                    builder.Append(" Action: ");
                    builder.Append(reservePressure.recommendedAction);
                }
            }

            return builder.ToString();
        }

        public string BuildHouseholdInspectionSummary(int householdId)
        {
            HouseholdState household = FindHouseholdById(householdId);
            if (household == null)
            {
                return "Household records unavailable.";
            }

            string baseSummary = household.BuildInspectionSummary();
            string healthSummary = BuildHouseholdHealthInspectionSegment(household);
            return string.IsNullOrWhiteSpace(healthSummary)
                ? baseSummary
                : $"{baseSummary} | {healthSummary}";
        }

        public string BuildHouseholdInspectionSummaryByHomeBuildingId(int homeBuildingId)
        {
            HouseholdState household = FindHouseholdByHomeBuildingId(homeBuildingId);
            if (household == null)
            {
                return "Household records unavailable.";
            }

            string baseSummary = household.BuildInspectionSummary();
            string healthSummary = BuildHouseholdHealthInspectionSegment(household);
            return string.IsNullOrWhiteSpace(healthSummary)
                ? baseSummary
                : $"{baseSummary} | {healthSummary}";
        }

        private HouseholdState FindHouseholdById(int householdId)
        {
            if (state == null || state.households == null || householdId <= 0)
            {
                return null;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household != null && household.id == householdId)
                {
                    household.EnsureHouseholdReservesInitialized();
                    household.EnsureHouseholdUpgradesInitialized();
                    return household;
                }
            }

            return null;
        }

        private HouseholdState FindHouseholdByHomeBuildingId(int homeBuildingId)
        {
            if (state == null || state.households == null || homeBuildingId < 0)
            {
                return null;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household != null && household.homeBuildingId == homeBuildingId)
                {
                    household.EnsureHouseholdReservesInitialized();
                    household.EnsureHouseholdUpgradesInitialized();
                    return household;
                }
            }

            return null;
        }

        public PopulationHealthDailySnapshot ResolveDailyHealth(int absoluteDayIndex, bool force = false)
        {
            if (state == null)
            {
                lastHealthSummary = "No population available for health resolution.";
                return new PopulationHealthDailySnapshot { summary = lastHealthSummary };
            }

            int day = Mathf.Max(0, absoluteDayIndex);
            if (!force && lastHealthResolutionDayIndex == day)
            {
                return BuildCurrentHealthSnapshot(lastHealthSummary);
            }

            AutoWireReferences();
            ResetDailyMedicalSpend();
            int newCases = TickExistingConditionsAndRollNewCases(day);
            PopulationHealthDailySnapshot doctorSnapshot = sharedBusinessRuntime != null
                ? sharedBusinessRuntime.ResolveDailyDoctorTreatments(state, day)
                : null;

            RecalculateHouseholdIncomeAndSummaries("Population health updated.");

            PopulationHealthDailySnapshot snapshot = BuildCurrentHealthSnapshot();
            snapshot.newCases = newCases;
            if (doctorSnapshot != null)
            {
                snapshot.treatedCases = doctorSnapshot.treatedCases;
                snapshot.untreatedCases = doctorSnapshot.untreatedCases;
                snapshot.medicalSpendCents = doctorSnapshot.medicalSpendCents;
                snapshot.doctorRevenueCents = doctorSnapshot.doctorRevenueCents;
            }
            else
            {
                snapshot.untreatedCases = snapshot.activeCases;
                snapshot.medicalSpendCents = GetDailyMedicalSpendCents();
                snapshot.doctorRevenueCents = 0;
            }

            snapshot.summary = BuildHealthSummary(snapshot);
            lastHealthResolutionDayIndex = day;
            lastHealthSummary = snapshot.summary;
            return snapshot;
        }

        public PopulationSaveDto CaptureSaveDto()
        {
            PopulationSaveDto dto = new();
            if (state == null)
            {
                return dto;
            }

            RefreshSettlementMetrics();
            if (state.people != null)
            {
                for (int i = 0; i < state.people.Count; i++)
                {
                    PersonState person = state.people[i];
                    if (person == null)
                    {
                        continue;
                    }

                    person.EnsureWorkerTraitsInitialized();
                    person.EnsureSettlementStateInitialized();
                    PopulationHealthEvaluator.EnsureHealthInitialized(person);
                    dto.people.Add(new PersonSaveDto
                    {
                        id = person.id,
                        firstName = person.firstName,
                        lastName = person.lastName,
                        age = person.age,
                        ageBand = person.ageBand,
                        laborAccessLevel = person.laborAccessLevel,
                        householdId = person.householdId,
                        professionId = person.professionId,
                        professionName = person.professionName,
                        wage = person.wage,
                        health = person.health != null ? person.health.Clone() : new PersonHealthState(),
                        visibleWorkerProfile = person.visibleWorkerProfile != null ? person.visibleWorkerProfile.Clone() : new WorkerVisibleProfile(),
                        hiddenWorkerTraits = person.hiddenWorkerTraits != null ? person.hiddenWorkerTraits.Clone() : new WorkerHiddenTraits(),
                        workerTraitVisibility = person.workerTraitVisibility != null ? person.workerTraitVisibility.Clone() : new WorkerTraitVisibility(),
                        apprenticeship = person.apprenticeship != null ? person.apprenticeship.Clone() : WorkerApprenticeshipState.FirstPassDefault(),
                        homeBuildingId = person.homeBuildingId,
                        workplaceBuildingId = person.workplaceBuildingId,
                        scheduleState = person.scheduleState,
                        currentDestinationBuildingId = person.currentDestinationBuildingId,
                        arrivalProfile = person.arrivalProfile,
                        settlementArrangement = person.settlementArrangement,
                        settlementPressure = person.settlementPressure,
                        startingCashCents = person.startingCashCents,
                        laborUrgency01 = person.laborUrgency01,
                        laborReadinessModifier = person.laborReadinessModifier,
                        preferredProfessionBias = person.preferredProfessionBias,
                        settlementDifficulty = person.settlementDifficulty,
                        hostHouseholdId = person.hostHouseholdId
                    });
                }
            }

            if (state.households != null)
            {
                for (int i = 0; i < state.households.Count; i++)
                {
                    HouseholdState household = state.households[i];
                    if (household == null)
                    {
                        continue;
                    }

                    household.EnsureHouseholdReservesInitialized();
                    household.EnsureSettlementStateInitialized();
                    bool isBoardingHouseGuestHousehold = household.isBoardingHouseGuestHousehold;
                    bool isBoardingHouseLodging = household.isBoardingHouseLodging || isBoardingHouseGuestHousehold;
                    string boardingBusinessInstanceId = !string.IsNullOrWhiteSpace(household.boardingBusinessInstanceId)
                        ? household.boardingBusinessInstanceId
                        : household.lodgingBusinessInstanceId ?? string.Empty;

                    HouseholdSaveDto householdDto = new()
                    {
                        id = household.id,
                        householdName = household.householdName,
                        surname = household.surname,
                        homeBuildingId = household.homeBuildingId,
                        weeklyIncomeSnapshot = household.weeklyIncomeSnapshot,
                        spendingMoneyCents = household.spendingMoneyCents,
                        lastStoreSpendCents = household.lastStoreSpendCents,
                        lifetimeStoreSpendCents = household.lifetimeStoreSpendCents,
                        demandSnapshot = household.demandSnapshot,
                        foodReserveUnits = household.foodReserveUnits,
                        lastReserveDepletionDayIndex = household.lastReserveDepletionDayIndex,
                        lastDailyReserveUseUnits = household.lastDailyReserveUseUnits,
                        lastDailyReserveLocalPurchaseUnits = household.lastDailyReserveLocalPurchaseUnits,
                        lastDailyReserveOffMapPurchaseUnits = household.lastDailyReserveOffMapPurchaseUnits,
                        lastWeeklyUpgradeIncomeCents = household.lastWeeklyUpgradeIncomeCents,
                        lastWeeklyUpgradeUpkeepCents = household.lastWeeklyUpgradeUpkeepCents,
                        lastWeeklyReserveDeltaUnits = household.lastWeeklyReserveDeltaUnits,
                        lastDailyMedicalSpendCents = household.lastDailyMedicalSpendCents,
                        lastWeeklyLaborLossCents = household.lastWeeklyLaborLossCents,
                        lifetimeMedicalSpendCents = household.lifetimeMedicalSpendCents,
                        heatRetentionTier = household.heatRetentionTier,
                        stoveTier = household.stoveTier,
                        preferredFuel = household.preferredFuel,
                        lastWinterStrain01 = household.lastWinterStrain01,
                        lastHeatingPressure01 = household.lastHeatingPressure01,
                        lastHeatingFuelUseUnits = household.lastHeatingFuelUseUnits,
                        lastHeatingFuelShortfallUnits = household.lastHeatingFuelShortfallUnits,
                        arrivalProfile = household.arrivalProfile,
                        settlementArrangement = household.settlementArrangement,
                        dwellingKind = household.dwellingKind,
                        dwellingSummary = household.dwellingSummary ?? string.Empty,
                        settlementPressure = household.settlementPressure,
                        settlementReserveStrengthCents = household.settlementReserveStrengthCents,
                        baseBoardingCapacity = household.baseBoardingCapacity,
                        boardingCapacity = household.boardingCapacity,
                        hostsBoarders = household.hostsBoarders,
                        isRenterHousehold = household.isRenterHousehold,
                        hasKinAbsorptionPressure = household.hasKinAbsorptionPressure,
                        isTransientHousehold = household.isTransientHousehold,
                        isBoardingHouseLodging = isBoardingHouseLodging,
                        boardingBusinessInstanceId = boardingBusinessInstanceId,
                        isBoardingHouseGuestHousehold = isBoardingHouseGuestHousehold,
                        lodgingBusinessInstanceId = boardingBusinessInstanceId,
                        crowdingPressure01 = household.crowdingPressure01,
                        boarderOverloadPressure01 = household.boarderOverloadPressure01,
                        kinAbsorptionPressure01 = household.kinAbsorptionPressure01,
                        lastSettlementSummary = household.lastSettlementSummary
                    };

                    if (household.memberIds != null)
                    {
                        for (int memberIndex = 0; memberIndex < household.memberIds.Count; memberIndex++)
                        {
                            householdDto.memberIds.Add(household.memberIds[memberIndex]);
                        }
                    }

                    if (household.boarderPersonIds != null)
                    {
                        for (int boarderIndex = 0; boarderIndex < household.boarderPersonIds.Count; boarderIndex++)
                        {
                            householdDto.boarderPersonIds.Add(household.boarderPersonIds[boarderIndex]);
                        }
                    }

                    household.EnsureHouseholdUpgradesInitialized();
                    if (household.upgrades != null)
                    {
                        for (int upgradeIndex = 0; upgradeIndex < household.upgrades.Count; upgradeIndex++)
                        {
                            HouseholdUpgradeState upgrade = household.upgrades[upgradeIndex];
                            if (upgrade == null)
                            {
                                continue;
                            }

                            householdDto.upgrades.Add(new HouseholdUpgradeSaveDto
                            {
                                kind = upgrade.kind,
                                built = upgrade.built,
                                builtDayIndex = upgrade.builtDayIndex
                            });
                        }
                    }

                    household.EnsureHouseholdReservesInitialized();
                    householdDto.foodReserveUnits = household.foodReserveUnits;
                    if (household.reserves != null)
                    {
                        for (int reserveIndex = 0; reserveIndex < household.reserves.Count; reserveIndex++)
                        {
                            HouseholdReserveState reserve = household.reserves[reserveIndex];
                            if (reserve == null)
                            {
                                continue;
                            }

                            householdDto.reserves.Add(new HouseholdReserveSaveDto
                            {
                                categoryId = reserve.categoryId,
                                displayName = reserve.displayName,
                                currentUnits = reserve.currentUnits,
                                targetUnits = reserve.targetUnits,
                                lowThresholdUnits = reserve.lowThresholdUnits,
                                lastDailyUseUnits = reserve.lastDailyUseUnits,
                                lastRequestedPurchaseUnits = reserve.lastRequestedPurchaseUnits,
                                lastLocalPurchaseUnits = reserve.lastLocalPurchaseUnits,
                                lastOffMapPurchaseUnits = reserve.lastOffMapPurchaseUnits,
                                lastUrgency01 = reserve.lastUrgency01,
                                lastShoppingAttemptDayIndex = reserve.lastShoppingAttemptDayIndex,
                                consecutiveLocalShortfallDays = reserve.consecutiveLocalShortfallDays,
                                rememberedStockoutUnits = reserve.rememberedStockoutUnits,
                                stockoutFrustration01 = reserve.stockoutFrustration01
                            });
                        }
                    }

                    dto.households.Add(householdDto);
                }
            }

            if (state.validationMessages != null)
            {
                for (int i = 0; i < state.validationMessages.Count; i++)
                {
                    dto.validationMessages.Add(state.validationMessages[i]);
                }
            }

            if (state.rentalProperties != null)
            {
                for (int i = 0; i < state.rentalProperties.Count; i++)
                {
                    RentalPropertyState property = state.rentalProperties[i];
                    if (property == null)
                    {
                        continue;
                    }

                    property.Sanitize();
                    dto.rentalProperties.Add(new RentalPropertySaveDto
                    {
                        buildingId = property.buildingId,
                        displayName = property.displayName,
                        residentHouseholdCapacity = property.residentHouseholdCapacity,
                        occupiedHouseholds = property.occupiedHouseholds,
                        vacantHouseholdSlots = property.vacantHouseholdSlots,
                        playerOwned = property.playerOwned,
                        mixedUse = property.mixedUse,
                        rentalCapable = property.rentalCapable,
                        occupancySummary = property.occupancySummary
                    });
                }
            }

            if (state.rentalApplicants != null)
            {
                for (int i = 0; i < state.rentalApplicants.Count; i++)
                {
                    RentalApplicantState applicant = state.rentalApplicants[i];
                    if (applicant == null)
                    {
                        continue;
                    }

                    applicant.Sanitize();
                    dto.rentalApplicants.Add(new RentalApplicantSaveDto
                    {
                        personId = applicant.personId,
                        householdId = applicant.householdId,
                        preferredBuildingId = applicant.preferredBuildingId,
                        displayName = applicant.displayName,
                        arrivalProfile = applicant.arrivalProfile,
                        currentArrangement = applicant.currentArrangement,
                        savingsCents = applicant.savingsCents,
                        weeklyIncomeCents = applicant.weeklyIncomeCents,
                        weeksWaiting = applicant.weeksWaiting,
                        score01 = applicant.score01,
                        acceptedLastWeek = applicant.acceptedLastWeek,
                        status = applicant.status
                    });
                }
            }

            dto.lastHealthResolutionDayIndex = lastHealthResolutionDayIndex;
            dto.lastHealthSummary = lastHealthSummary ?? string.Empty;
            dto.boardingCapacity = state.boardingCapacity;
            dto.boardingUsed = state.boardingUsed;
            dto.boardingHouseCapacity = state.boardingHouseCapacity;
            dto.boardingHouseUsed = state.boardingHouseUsed;
            dto.renterHouseholdCount = state.renterHouseholdCount;
            dto.kinPlacementCount = state.kinPlacementCount;
            dto.transientPersonCount = state.transientPersonCount;
            dto.unstableHouseholdCount = state.unstableHouseholdCount;
            dto.departedUnsettledCount = state.departedUnsettledCount;
            dto.rentalVacancyCount = state.rentalVacancyCount;
            dto.playerOwnedRentalVacancyCount = state.playerOwnedRentalVacancyCount;
            dto.rentalApplicantCount = state.rentalApplicantCount;
            dto.strongerHousingNeedCount = state.strongerHousingNeedCount;
            dto.housingPressure01 = state.housingPressure01;
            dto.rentalPressure01 = state.rentalPressure01;
            dto.laborAbsorption01 = state.laborAbsorption01;
            dto.lastSettlementSummary = state.lastSettlementSummary ?? string.Empty;
            dto.lastRentalSummary = state.lastRentalSummary ?? string.Empty;
            return dto;
        }

        public void LoadFromSaveDto(PopulationSaveDto dto)
        {
            state = new PopulationState();
            if (dto == null)
            {
                lastHealthResolutionDayIndex = -1;
                lastHealthSummary = "No health resolution yet.";
                RefreshSummaryFields("Population save data was missing.");
                return;
            }

            if (dto.people != null)
            {
                for (int i = 0; i < dto.people.Count; i++)
                {
                    PersonSaveDto personDto = dto.people[i];
                    if (personDto == null)
                    {
                        continue;
                    }

                    PersonState person = new()
                    {
                        id = personDto.id,
                        firstName = personDto.firstName,
                        lastName = personDto.lastName,
                        age = personDto.age,
                        ageBand = personDto.ageBand,
                        laborAccessLevel = personDto.laborAccessLevel,
                        householdId = personDto.householdId,
                        professionId = personDto.professionId,
                        professionName = personDto.professionName,
                        wage = personDto.wage,
                        health = personDto.health != null ? personDto.health.Clone() : new PersonHealthState(),
                        visibleWorkerProfile = personDto.visibleWorkerProfile != null ? personDto.visibleWorkerProfile.Clone() : new WorkerVisibleProfile(),
                        hiddenWorkerTraits = personDto.hiddenWorkerTraits != null ? personDto.hiddenWorkerTraits.Clone() : new WorkerHiddenTraits(),
                        workerTraitVisibility = personDto.workerTraitVisibility != null ? personDto.workerTraitVisibility.Clone() : new WorkerTraitVisibility(),
                        apprenticeship = personDto.apprenticeship != null ? personDto.apprenticeship.Clone() : WorkerApprenticeshipState.FirstPassDefault(),
                        homeBuildingId = personDto.homeBuildingId,
                        workplaceBuildingId = personDto.workplaceBuildingId,
                        scheduleState = personDto.scheduleState,
                        currentDestinationBuildingId = personDto.currentDestinationBuildingId,
                        arrivalProfile = personDto.arrivalProfile,
                        settlementArrangement = personDto.settlementArrangement,
                        settlementPressure = personDto.settlementPressure,
                        startingCashCents = Mathf.Max(0, personDto.startingCashCents),
                        laborUrgency01 = Mathf.Clamp01(personDto.laborUrgency01),
                        laborReadinessModifier = personDto.laborReadinessModifier,
                        preferredProfessionBias = personDto.preferredProfessionBias,
                        settlementDifficulty = Mathf.Max(0, personDto.settlementDifficulty),
                        hostHouseholdId = personDto.hostHouseholdId
                    };

                    person.EnsureWorkerTraitsInitialized(1000 + person.id);
                    person.EnsureApprenticeshipInitialized();
                    person.EnsureSettlementStateInitialized();
                    PopulationHealthEvaluator.EnsureHealthInitialized(person);
                    state.people.Add(person);
                }
            }

            if (dto.households != null)
            {
                for (int i = 0; i < dto.households.Count; i++)
                {
                    HouseholdSaveDto householdDto = dto.households[i];
                    if (householdDto == null)
                    {
                        continue;
                    }

                    bool savedBoardingHouseGuestHousehold = householdDto.isBoardingHouseGuestHousehold;
                    bool savedBoardingHouseLodging = householdDto.isBoardingHouseLodging || savedBoardingHouseGuestHousehold;
                    string savedBoardingBusinessInstanceId = !string.IsNullOrWhiteSpace(householdDto.boardingBusinessInstanceId)
                        ? householdDto.boardingBusinessInstanceId
                        : householdDto.lodgingBusinessInstanceId ?? string.Empty;

                    HouseholdState household = new()
                    {
                        id = householdDto.id,
                        householdName = householdDto.householdName,
                        surname = householdDto.surname,
                        homeBuildingId = householdDto.homeBuildingId,
                        weeklyIncomeSnapshot = householdDto.weeklyIncomeSnapshot,
                        spendingMoneyCents = householdDto.spendingMoneyCents,
                        lastStoreSpendCents = householdDto.lastStoreSpendCents,
                        lifetimeStoreSpendCents = householdDto.lifetimeStoreSpendCents,
                        demandSnapshot = householdDto.demandSnapshot,
                        foodReserveUnits = Mathf.Max(0, householdDto.foodReserveUnits),
                        lastReserveDepletionDayIndex = householdDto.lastReserveDepletionDayIndex,
                        lastDailyReserveUseUnits = Mathf.Max(0, householdDto.lastDailyReserveUseUnits),
                        lastDailyReserveLocalPurchaseUnits = Mathf.Max(0, householdDto.lastDailyReserveLocalPurchaseUnits),
                        lastDailyReserveOffMapPurchaseUnits = Mathf.Max(0, householdDto.lastDailyReserveOffMapPurchaseUnits),
                        lastWeeklyUpgradeIncomeCents = Mathf.Max(0, householdDto.lastWeeklyUpgradeIncomeCents),
                        lastWeeklyUpgradeUpkeepCents = Mathf.Max(0, householdDto.lastWeeklyUpgradeUpkeepCents),
                        lastWeeklyReserveDeltaUnits = householdDto.lastWeeklyReserveDeltaUnits,
                        lastDailyMedicalSpendCents = Mathf.Max(0, householdDto.lastDailyMedicalSpendCents),
                        lastWeeklyLaborLossCents = Mathf.Max(0, householdDto.lastWeeklyLaborLossCents),
                        lifetimeMedicalSpendCents = Mathf.Max(0, householdDto.lifetimeMedicalSpendCents),
                        heatRetentionTier = householdDto.heatRetentionTier,
                        stoveTier = householdDto.stoveTier,
                        preferredFuel = householdDto.preferredFuel,
                        lastWinterStrain01 = Mathf.Clamp01(householdDto.lastWinterStrain01),
                        lastHeatingPressure01 = Mathf.Clamp01(householdDto.lastHeatingPressure01),
                        lastHeatingFuelUseUnits = Mathf.Max(0, householdDto.lastHeatingFuelUseUnits),
                        lastHeatingFuelShortfallUnits = Mathf.Max(0, householdDto.lastHeatingFuelShortfallUnits),
                        arrivalProfile = householdDto.arrivalProfile,
                        settlementArrangement = householdDto.settlementArrangement,
                        dwellingKind = householdDto.dwellingKind,
                        dwellingSummary = householdDto.dwellingSummary ?? string.Empty,
                        settlementPressure = householdDto.settlementPressure,
                        settlementReserveStrengthCents = Mathf.Max(0, householdDto.settlementReserveStrengthCents),
                        baseBoardingCapacity = Mathf.Max(0, householdDto.baseBoardingCapacity),
                        boardingCapacity = Mathf.Max(0, householdDto.boardingCapacity),
                        hostsBoarders = householdDto.hostsBoarders,
                        isRenterHousehold = householdDto.isRenterHousehold,
                        hasKinAbsorptionPressure = householdDto.hasKinAbsorptionPressure,
                        isTransientHousehold = householdDto.isTransientHousehold,
                        isBoardingHouseLodging = savedBoardingHouseLodging,
                        boardingBusinessInstanceId = savedBoardingBusinessInstanceId,
                        isBoardingHouseGuestHousehold = savedBoardingHouseGuestHousehold,
                        lodgingBusinessInstanceId = savedBoardingBusinessInstanceId,
                        crowdingPressure01 = Mathf.Clamp01(householdDto.crowdingPressure01),
                        boarderOverloadPressure01 = Mathf.Clamp01(householdDto.boarderOverloadPressure01),
                        kinAbsorptionPressure01 = Mathf.Clamp01(householdDto.kinAbsorptionPressure01),
                        lastSettlementSummary = householdDto.lastSettlementSummary
                    };

                    if (householdDto.memberIds != null)
                    {
                        for (int memberIndex = 0; memberIndex < householdDto.memberIds.Count; memberIndex++)
                        {
                            household.memberIds.Add(householdDto.memberIds[memberIndex]);
                        }
                    }

                    if (householdDto.boarderPersonIds != null)
                    {
                        for (int boarderIndex = 0; boarderIndex < householdDto.boarderPersonIds.Count; boarderIndex++)
                        {
                            household.boarderPersonIds.Add(householdDto.boarderPersonIds[boarderIndex]);
                        }
                    }

                    if (householdDto.upgrades != null)
                    {
                        for (int upgradeIndex = 0; upgradeIndex < householdDto.upgrades.Count; upgradeIndex++)
                        {
                            HouseholdUpgradeSaveDto upgradeDto = householdDto.upgrades[upgradeIndex];
                            if (upgradeDto == null)
                            {
                                continue;
                            }

                            household.upgrades.Add(new HouseholdUpgradeState
                            {
                                kind = upgradeDto.kind,
                                built = upgradeDto.built,
                                builtDayIndex = Mathf.Max(0, upgradeDto.builtDayIndex)
                            });
                        }
                    }

                    if (householdDto.reserves != null)
                    {
                        for (int reserveIndex = 0; reserveIndex < householdDto.reserves.Count; reserveIndex++)
                        {
                            HouseholdReserveSaveDto reserveDto = householdDto.reserves[reserveIndex];
                            if (reserveDto == null)
                            {
                                continue;
                            }

                            household.reserves.Add(new HouseholdReserveState
                            {
                                categoryId = reserveDto.categoryId,
                                displayName = reserveDto.displayName,
                                currentUnits = Mathf.Max(0, reserveDto.currentUnits),
                                targetUnits = Mathf.Max(0, reserveDto.targetUnits),
                                lowThresholdUnits = Mathf.Max(0, reserveDto.lowThresholdUnits),
                                lastDailyUseUnits = Mathf.Max(0, reserveDto.lastDailyUseUnits),
                                lastRequestedPurchaseUnits = Mathf.Max(0, reserveDto.lastRequestedPurchaseUnits),
                                lastLocalPurchaseUnits = Mathf.Max(0, reserveDto.lastLocalPurchaseUnits),
                                lastOffMapPurchaseUnits = Mathf.Max(0, reserveDto.lastOffMapPurchaseUnits),
                                lastUrgency01 = Mathf.Clamp01(reserveDto.lastUrgency01),
                                lastShoppingAttemptDayIndex = Mathf.Max(-1, reserveDto.lastShoppingAttemptDayIndex),
                                consecutiveLocalShortfallDays = Mathf.Max(0, reserveDto.consecutiveLocalShortfallDays),
                                rememberedStockoutUnits = Mathf.Max(0, reserveDto.rememberedStockoutUnits),
                                stockoutFrustration01 = Mathf.Clamp01(reserveDto.stockoutFrustration01)
                            });
                        }
                    }

                    household.EnsureHouseholdUpgradesInitialized();
                    household.EnsureHouseholdReservesInitialized(true, householdDto.reserves == null || householdDto.reserves.Count == 0);
                    HouseholdUpgradeCatalog.ClampReserveToCapacity(household);
                    household.EnsureSettlementStateInitialized();
                    state.households.Add(household);
                }
            }

            if (dto.validationMessages != null)
            {
                for (int i = 0; i < dto.validationMessages.Count; i++)
                {
                    state.validationMessages.Add(dto.validationMessages[i]);
                }
            }

            LoadRentalStateFromSaveDto(dto);

            lastHealthResolutionDayIndex = dto.lastHealthResolutionDayIndex;
            lastHealthSummary = string.IsNullOrWhiteSpace(dto.lastHealthSummary)
                ? "No health resolution yet."
                : dto.lastHealthSummary;
            NormalizeTransientScheduleStates();
            RecalculateHouseholdIncomeSnapshots();
            ApplySettlementMetricsFromSaveDto(dto);
            RefreshSummaryFields("Population restored from save.");
        }

        public int NormalizeTransientScheduleStates()
        {
            if (state == null || state.people == null)
            {
                return 0;
            }

            int normalized = 0;
            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                if (person == null)
                {
                    continue;
                }

                if (TryGetArrivalScheduleState(person.scheduleState, out PopulationScheduleState stableState))
                {
                    person.scheduleState = stableState;
                    person.currentDestinationBuildingId = GetStableDestinationBuildingId(person, stableState);
                    normalized++;
                    continue;
                }

                int stableDestination = GetStableDestinationBuildingId(person, person.scheduleState);
                if (stableDestination >= 0 && person.currentDestinationBuildingId != stableDestination)
                {
                    person.currentDestinationBuildingId = stableDestination;
                    normalized++;
                }
            }

            return normalized;
        }

        public static bool TryGetArrivalScheduleState(PopulationScheduleState scheduleState, out PopulationScheduleState arrivalState)
        {
            switch (scheduleState)
            {
                case PopulationScheduleState.GoingToWork:
                    arrivalState = PopulationScheduleState.AtWork;
                    return true;
                case PopulationScheduleState.GoingShopping:
                    arrivalState = PopulationScheduleState.AtStore;
                    return true;
                case PopulationScheduleState.ReturningHome:
                    arrivalState = PopulationScheduleState.AtHome;
                    return true;
                default:
                    arrivalState = scheduleState;
                    return false;
            }
        }

        private static int GetStableDestinationBuildingId(PersonState person, PopulationScheduleState scheduleState)
        {
            if (person == null)
            {
                return -1;
            }

            return scheduleState switch
            {
                PopulationScheduleState.AtHome => person.homeBuildingId,
                PopulationScheduleState.AtWork => person.workplaceBuildingId >= 0
                    ? person.workplaceBuildingId
                    : person.currentDestinationBuildingId,
                _ => person.currentDestinationBuildingId
            };
        }

        private int TickExistingConditionsAndRollNewCases(int absoluteDayIndex)
        {
            if (state == null || state.people == null)
            {
                return 0;
            }

            int newCases = 0;
            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                if (person == null)
                {
                    continue;
                }

                PersonHealthState health = PopulationHealthEvaluator.EnsureHealthInitialized(person);
                bool hadActiveCondition = health.HasActiveCondition;
                health.TickNaturalRecovery();
                if (hadActiveCondition || health.HasActiveCondition)
                {
                    continue;
                }

                HouseholdState household = state.GetHousehold(person.householdId);
                if (TryRollNewCondition(person, household, absoluteDayIndex, out HealthConditionKind kind, out HealthConditionSeverity severity))
                {
                    int durationDays = PopulationHealthEvaluator.GetDurationDays(kind, severity);
                    string summary = $"New {severity.ToString().ToLowerInvariant()} {kind.ToString().ToLowerInvariant()}.";
                    health.ApplyCondition(kind, severity, durationDays, summary);
                    newCases++;
                }
            }

            return newCases;
        }

        private static bool TryRollNewCondition(
            PersonState person,
            HouseholdState household,
            int absoluteDayIndex,
            out HealthConditionKind kind,
            out HealthConditionSeverity severity)
        {
            kind = HealthConditionKind.None;
            severity = HealthConditionSeverity.None;
            if (person == null)
            {
                return false;
            }

            int illnessRoll = BuildDailyHealthRoll(person, absoluteDayIndex, 101);
            int illnessRisk = PopulationHealthEvaluator.GetIllnessRiskPerTenThousand(person, household);
            if (illnessRoll < illnessRisk)
            {
                kind = HealthConditionKind.Illness;
                severity = BuildDailyHealthRoll(person, absoluteDayIndex, 103) < 1500
                    ? HealthConditionSeverity.Serious
                    : HealthConditionSeverity.Mild;
                return true;
            }

            if (PopulationHealthEvaluator.IsLaborMarketWorker(person)
                && BuildDailyHealthRoll(person, absoluteDayIndex, 107) < PopulationHealthEvaluator.GetInjuryRiskPerTenThousand(person))
            {
                kind = HealthConditionKind.Injury;
                severity = BuildDailyHealthRoll(person, absoluteDayIndex, 109) < 1500
                    ? HealthConditionSeverity.Serious
                    : HealthConditionSeverity.Mild;
                return true;
            }

            return false;
        }

        private static int BuildDailyHealthRoll(PersonState person, int absoluteDayIndex, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)person.id) * 16777619u;
                hash = (hash ^ (uint)person.householdId) * 16777619u;
                hash = (hash ^ (uint)Mathf.Max(0, absoluteDayIndex)) * 16777619u;
                hash = (hash ^ (uint)salt) * 16777619u;
                return (int)(hash % 10000u);
            }
        }

        private void ResetDailyMedicalSpend()
        {
            if (state == null || state.households == null)
            {
                return;
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household != null)
                {
                    household.lastDailyMedicalSpendCents = 0;
                }
            }
        }

        private void InitializeHealthDefaults()
        {
            if (state == null)
            {
                return;
            }

            if (state.people != null)
            {
                for (int i = 0; i < state.people.Count; i++)
                {
                    PopulationHealthEvaluator.EnsureHealthInitialized(state.people[i]);
                    state.people[i]?.EnsureSettlementStateInitialized();
                }
            }

            if (state.households != null)
            {
                for (int i = 0; i < state.households.Count; i++)
                {
                    HouseholdState household = state.households[i];
                    if (household == null)
                    {
                        continue;
                    }

                    household.lastDailyMedicalSpendCents = Mathf.Max(0, household.lastDailyMedicalSpendCents);
                    household.lastWeeklyLaborLossCents = Mathf.Max(0, household.lastWeeklyLaborLossCents);
                    household.lifetimeMedicalSpendCents = Mathf.Max(0, household.lifetimeMedicalSpendCents);
                    household.EnsureSettlementStateInitialized();
                }
            }
        }

        private PopulationHealthDailySnapshot BuildCurrentHealthSnapshot(string summary = null)
        {
            PopulationHealthDailySnapshot snapshot = new()
            {
                activeCases = CountActiveHealthCases(),
                laborLossCents = GetWeeklyLaborLossCents(),
                medicalSpendCents = GetDailyMedicalSpendCents(),
                doctorRevenueCents = 0,
                summary = summary ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(snapshot.summary))
            {
                snapshot.summary = BuildHealthSummary(snapshot);
            }

            return snapshot;
        }

        private int CountActiveHealthCases()
        {
            if (state == null || state.people == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < state.people.Count; i++)
            {
                if (PopulationHealthEvaluator.HasActiveCondition(state.people[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private int GetWeeklyLaborLossCents()
        {
            if (state == null || state.households == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < state.households.Count; i++)
            {
                total += state.households[i] != null ? Mathf.Max(0, state.households[i].lastWeeklyLaborLossCents) : 0;
            }

            return total;
        }

        private int GetDailyMedicalSpendCents()
        {
            if (state == null || state.households == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < state.households.Count; i++)
            {
                total += state.households[i] != null ? Mathf.Max(0, state.households[i].lastDailyMedicalSpendCents) : 0;
            }

            return total;
        }

        private static string BuildHealthSummary(PopulationHealthDailySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "No health resolution yet.";
            }

            return $"Health: active {snapshot.activeCases}, new {snapshot.newCases}, treated {snapshot.treatedCases}, untreated {snapshot.untreatedCases}, labor loss {FormatMoney(snapshot.laborLossCents)}, medical spend {FormatMoney(snapshot.medicalSpendCents)}, doctor revenue {FormatMoney(snapshot.doctorRevenueCents)}.";
        }

        private static string FormatMoney(int cents)
        {
            return $"${Mathf.Max(0, cents) / 100f:0.00}";
        }

        private int BuildWeeklySettlementSeed()
        {
            unchecked
            {
                int seed = 7349;
                seed = seed * 31 + (townWorld != null && townWorld.Settings != null ? townWorld.Settings.seed : 0);
                seed = seed * 31 + (timeManager != null ? Mathf.Max(0, timeManager.CurrentWeek) : 0);
                seed = seed * 31 + (state != null && state.people != null ? state.people.Count : 0);
                return seed;
            }
        }

        private void Reset()
        {
            AutoWireReferences();
        }

        private void Awake()
        {
            AutoWireReferences();
        }

        private void OnEnable()
        {
            SubscribeToTime();
        }

        private void Start()
        {
            SubscribeToTime();
            if (generateOnStart && Application.isPlaying)
            {
                GeneratePopulationSnapshot();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromTime();
        }

        private void OnValidate()
        {
            if (previewHouseholdLimit < 1)
            {
                previewHouseholdLimit = 1;
            }

            settings?.Sanitize();
            RefreshSummaryFields(lastGenerationSummary);
        }

        private void AutoWireReferences()
        {
            if (townWorld == null)
            {
                townWorld = FindAnyObjectByType<TownWorldController>();
            }

            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
        }

        private void SubscribeToTime()
        {
            AutoWireReferences();
            if (subscribedToTime || timeManager == null)
            {
                return;
            }

            timeManager.DayChanged += OnDayChanged;
            subscribedToTime = true;
        }

        private void UnsubscribeFromTime()
        {
            if (!subscribedToTime || timeManager == null)
            {
                subscribedToTime = false;
                return;
            }

            timeManager.DayChanged -= OnDayChanged;
            subscribedToTime = false;
        }

        private void OnDayChanged(SimulationDateChangedContext context)
        {
            int absoluteDayIndex = context.CurrentDate.AbsoluteDayIndex;
            ResolveDailyHealth(absoluteDayIndex);

            int weekIndex = Mathf.Max(0, absoluteDayIndex / 7);
            if (weekIndex != lastSettlementResolutionWeekIndex)
            {
                ResolveWeeklySettlementProgression();
                lastSettlementResolutionWeekIndex = weekIndex;
            }
        }

        private void RecalculateHouseholdIncomeSnapshots()
        {
            if (state == null || state.households == null)
            {
                return;
            }

            for (int householdIndex = 0; householdIndex < state.households.Count; householdIndex++)
            {
                HouseholdState household = state.households[householdIndex];
                if (household == null)
                {
                    continue;
                }

                int weeklyIncome = 0;
                int weeklyLaborLossCents = 0;
                household.memberIds ??= new System.Collections.Generic.List<int>();

                for (int memberIndex = 0; memberIndex < household.memberIds.Count; memberIndex++)
                {
                    PersonState person = state.GetPerson(household.memberIds[memberIndex]);
                    if (person != null)
                    {
                        weeklyIncome += PopulationHealthEvaluator.GetEffectiveWeeklyWageDollars(person, household, state);
                        weeklyLaborLossCents += PopulationHealthEvaluator.GetWeeklyLaborLossCents(person, household, state);
                    }
                }

                household.weeklyIncomeSnapshot = weeklyIncome;
                household.lastWeeklyLaborLossCents = weeklyLaborLossCents;
            }
        }

        private void ApplySettlementMetricsFromSaveDto(PopulationSaveDto dto)
        {
            if (state == null || dto == null)
            {
                lastSettlementSummary = string.Empty;
                return;
            }

            if (!HasSavedSettlementMetrics(dto))
            {
                NewcomerSettlementPlanner.RefreshSettlementMetrics(
                    state,
                    NewcomerSettlementPlanner.BuildHomeOptions(townWorld, settings),
                    System.Array.Empty<NewcomerSettlementBoardingOption>());
                lastSettlementSummary = state.lastSettlementSummary ?? string.Empty;
                return;
            }

            state.boardingCapacity = Mathf.Max(0, dto.boardingCapacity);
            state.boardingUsed = Mathf.Max(0, dto.boardingUsed);
            state.boardingHouseCapacity = Mathf.Max(0, dto.boardingHouseCapacity);
            state.boardingHouseUsed = Mathf.Max(0, dto.boardingHouseUsed);
            state.renterHouseholdCount = Mathf.Max(0, dto.renterHouseholdCount);
            state.kinPlacementCount = Mathf.Max(0, dto.kinPlacementCount);
            state.transientPersonCount = Mathf.Max(0, dto.transientPersonCount);
            state.unstableHouseholdCount = Mathf.Max(0, dto.unstableHouseholdCount);
            state.departedUnsettledCount = Mathf.Max(0, dto.departedUnsettledCount);
            state.rentalVacancyCount = Mathf.Max(0, dto.rentalVacancyCount);
            state.playerOwnedRentalVacancyCount = Mathf.Max(0, dto.playerOwnedRentalVacancyCount);
            state.rentalApplicantCount = Mathf.Max(0, dto.rentalApplicantCount);
            state.strongerHousingNeedCount = Mathf.Max(0, dto.strongerHousingNeedCount);
            state.housingPressure01 = Mathf.Clamp01(dto.housingPressure01);
            state.rentalPressure01 = Mathf.Clamp01(dto.rentalPressure01);
            state.laborAbsorption01 = Mathf.Clamp01(dto.laborAbsorption01);
            state.lastSettlementSummary = dto.lastSettlementSummary ?? string.Empty;
            state.lastRentalSummary = dto.lastRentalSummary ?? string.Empty;
            state.EnsureSettlementMetricsInitialized();
            NewcomerSettlementPlanner.SyncSettlementValidationMessages(state);
            if (!HasSavedRentalMetrics(dto))
            {
                NewcomerSettlementPlanner.RefreshRentalMetrics(
                    state,
                    NewcomerSettlementPlanner.BuildHomeOptions(townWorld, settings));
            }

            state.settlementPressureSnapshot = NewcomerSettlementPlanner.BuildSettlementPressureSnapshot(state);
            lastSettlementSummary = state.lastSettlementSummary;
        }

        private void LoadRentalStateFromSaveDto(PopulationSaveDto dto)
        {
            if (state == null || dto == null)
            {
                return;
            }

            state.rentalProperties.Clear();
            if (dto.rentalProperties != null)
            {
                for (int i = 0; i < dto.rentalProperties.Count; i++)
                {
                    RentalPropertySaveDto propertyDto = dto.rentalProperties[i];
                    if (propertyDto == null)
                    {
                        continue;
                    }

                    RentalPropertyState property = new()
                    {
                        buildingId = propertyDto.buildingId,
                        displayName = propertyDto.displayName,
                        residentHouseholdCapacity = Mathf.Max(0, propertyDto.residentHouseholdCapacity),
                        occupiedHouseholds = Mathf.Max(0, propertyDto.occupiedHouseholds),
                        vacantHouseholdSlots = Mathf.Max(0, propertyDto.vacantHouseholdSlots),
                        playerOwned = propertyDto.playerOwned,
                        mixedUse = propertyDto.mixedUse,
                        rentalCapable = propertyDto.rentalCapable,
                        occupancySummary = propertyDto.occupancySummary
                    };
                    property.Sanitize();
                    state.rentalProperties.Add(property);
                }
            }

            state.rentalApplicants.Clear();
            if (dto.rentalApplicants != null)
            {
                for (int i = 0; i < dto.rentalApplicants.Count; i++)
                {
                    RentalApplicantSaveDto applicantDto = dto.rentalApplicants[i];
                    if (applicantDto == null)
                    {
                        continue;
                    }

                    RentalApplicantState applicant = new()
                    {
                        personId = applicantDto.personId,
                        householdId = applicantDto.householdId,
                        preferredBuildingId = applicantDto.preferredBuildingId,
                        displayName = applicantDto.displayName,
                        arrivalProfile = applicantDto.arrivalProfile,
                        currentArrangement = applicantDto.currentArrangement,
                        savingsCents = Mathf.Max(0, applicantDto.savingsCents),
                        weeklyIncomeCents = Mathf.Max(0, applicantDto.weeklyIncomeCents),
                        weeksWaiting = Mathf.Max(0, applicantDto.weeksWaiting),
                        score01 = Mathf.Clamp01(applicantDto.score01),
                        acceptedLastWeek = applicantDto.acceptedLastWeek,
                        status = applicantDto.status
                    };
                    applicant.Sanitize();
                    state.rentalApplicants.Add(applicant);
                }
            }

            state.EnsureSettlementMetricsInitialized();
        }

        private static bool HasSavedSettlementMetrics(PopulationSaveDto dto)
        {
            if (dto == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(dto.lastSettlementSummary)
                || dto.boardingCapacity != 0
                || dto.boardingUsed != 0
                || dto.boardingHouseCapacity != 0
                || dto.boardingHouseUsed != 0
                || dto.renterHouseholdCount != 0
                || dto.kinPlacementCount != 0
                || dto.transientPersonCount != 0
                || dto.unstableHouseholdCount != 0
                || dto.departedUnsettledCount != 0
                || dto.rentalVacancyCount != 0
                || dto.rentalApplicantCount != 0
                || dto.strongerHousingNeedCount != 0
                || HasSavedRentalMetrics(dto)
                || !Mathf.Approximately(dto.housingPressure01, 0f)
                || !Mathf.Approximately(dto.rentalPressure01, 0f)
                || (dto.laborAbsorption01 > 0f && !Mathf.Approximately(dto.laborAbsorption01, 1f));
        }

        private static bool HasSavedRentalMetrics(PopulationSaveDto dto)
        {
            if (dto == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(dto.lastRentalSummary)
                || dto.rentalVacancyCount != 0
                || dto.playerOwnedRentalVacancyCount != 0
                || dto.rentalApplicantCount != 0
                || dto.strongerHousingNeedCount != 0
                || !Mathf.Approximately(dto.rentalPressure01, 0f)
                || (dto.rentalProperties != null && dto.rentalProperties.Count > 0)
                || (dto.rentalApplicants != null && dto.rentalApplicants.Count > 0);
        }

        public TownReservePressureSnapshot RefreshTownReservePressure()
        {
            if (state == null)
            {
                return TownReservePressureSnapshot.Empty();
            }

            state.EnsureSettlementMetricsInitialized();
            TownReservePressureSnapshot snapshot = BuildTownReservePressureSnapshot();
            snapshot.Sanitize();
            state.townReservePressureSnapshot = snapshot;
            state.lastTownReserveSummary = BuildTownReserveSummary(snapshot);
            return snapshot;
        }

        private TownReservePressureSnapshot BuildTownReservePressureSnapshot()
        {
            TownReservePressureSnapshot snapshot = TownReservePressureSnapshot.Empty();
            if (state == null || state.households == null || state.households.Count == 0)
            {
                snapshot.initialized = true;
                snapshot.headline = "Town reserve pressure unavailable.";
                snapshot.primaryCause = "No active household reserve records are available.";
                snapshot.recommendedAction = string.Empty;
                return snapshot;
            }

            Dictionary<string, int> categoryHouseholds = new();
            Dictionary<string, string> categoryNames = new();
            int tracked = 0;
            int low = 0;
            int critical = 0;
            int stockout = 0;
            int food = 0;
            int fuel = 0;
            int medicine = 0;
            int requested = 0;
            int remembered = 0;
            int maxShortfallDays = 0;
            int severityPoints = 0;
            int strongestHouseholdScore = 0;

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                if (household == null || household.settlementArrangement == SettlementArrangement.Departed)
                {
                    continue;
                }

                HouseholdReservePressureSnapshot householdPressure = household.RefreshReservePressureSnapshot();
                if (householdPressure.trackedCategoryCount <= 0)
                {
                    continue;
                }

                tracked++;
                strongestHouseholdScore = Mathf.Max(strongestHouseholdScore, householdPressure.pressureScore);
                requested += Mathf.Max(0, householdPressure.requestedPurchaseUnits);
                remembered += Mathf.Max(0, householdPressure.rememberedStockoutUnits);
                maxShortfallDays = Mathf.Max(maxShortfallDays, householdPressure.maxConsecutiveLocalShortfallDays);

                switch (householdPressure.pressureBand)
                {
                    case HouseholdReservePressureBand.Stockout:
                        stockout++;
                        severityPoints += 3;
                        break;
                    case HouseholdReservePressureBand.Critical:
                        critical++;
                        severityPoints += 2;
                        break;
                    case HouseholdReservePressureBand.Low:
                        low++;
                        severityPoints += 1;
                        break;
                }

                if (householdPressure.foodPressure)
                {
                    food++;
                }

                if (householdPressure.fuelPressure)
                {
                    fuel++;
                }

                if (householdPressure.medicinePressure)
                {
                    medicine++;
                }

                if (householdPressure.pressureBand != HouseholdReservePressureBand.Stable
                    && !string.IsNullOrWhiteSpace(householdPressure.topPressureCategoryId))
                {
                    string key = householdPressure.topPressureCategoryId;
                    if (!categoryHouseholds.ContainsKey(key))
                    {
                        categoryHouseholds[key] = 0;
                        categoryNames[key] = string.IsNullOrWhiteSpace(householdPressure.topPressureCategoryName)
                            ? key
                            : householdPressure.topPressureCategoryName;
                    }

                    categoryHouseholds[key]++;
                }
            }

            snapshot.initialized = true;
            snapshot.trackedHouseholds = tracked;
            snapshot.trackedHouseholdCount = tracked;
            snapshot.lowReserveHouseholds = low;
            snapshot.lowReserveHouseholdCount = low;
            snapshot.criticalReserveHouseholds = critical;
            snapshot.criticalReserveHouseholdCount = critical;
            snapshot.stockoutMemoryHouseholds = stockout;
            snapshot.stockoutMemoryHouseholdCount = stockout;
            snapshot.foodPressureHouseholds = food;
            snapshot.foodPressureHouseholdCount = food;
            snapshot.fuelPressureHouseholds = fuel;
            snapshot.fuelPressureHouseholdCount = fuel;
            snapshot.medicinePressureHouseholds = medicine;
            snapshot.medicinePressureHouseholdCount = medicine;
            snapshot.totalRequestedPurchaseUnits = requested;
            snapshot.requestedPurchaseUnits = requested;
            snapshot.totalRememberedStockoutUnits = remembered;
            snapshot.rememberedStockoutUnits = remembered;
            snapshot.maxConsecutiveShortfallDays = maxShortfallDays;
            snapshot.maxConsecutiveLocalShortfallDays = maxShortfallDays;

            if (tracked <= 0)
            {
                snapshot.pressureBand = TownReservePressureBand.Stable;
                snapshot.pressure01 = 0f;
                snapshot.pressureScore = 0;
                snapshot.headline = "Town reserve pressure unavailable.";
                snapshot.primaryCause = "No active household reserve records are available.";
                return snapshot;
            }

            int affectedHouseholds = low + critical + stockout;
            float severityShare01 = Mathf.Clamp01(severityPoints / (float)Mathf.Max(1, tracked * 3));
            float affectedShare01 = Mathf.Clamp01(affectedHouseholds / (float)Mathf.Max(1, tracked));
            snapshot.pressure01 = Mathf.Clamp01(Mathf.Max(severityShare01, strongestHouseholdScore / 100f * 0.75f) + affectedShare01 * 0.18f);
            snapshot.pressureScore = Mathf.Clamp(Mathf.RoundToInt(snapshot.pressure01 * 100f), 0, 100);

            if (stockout >= 2 || (stockout > 0 && critical > 0) || snapshot.pressureScore >= 70)
            {
                snapshot.pressureBand = TownReservePressureBand.Urgent;
            }
            else if (critical > 0 || stockout > 0 || snapshot.pressureScore >= 38 || affectedShare01 >= 0.35f)
            {
                snapshot.pressureBand = TownReservePressureBand.Pressure;
            }
            else if (low > 0 || requested > 0 || remembered > 0 || snapshot.pressureScore >= 15)
            {
                snapshot.pressureBand = TownReservePressureBand.Watch;
            }
            else
            {
                snapshot.pressureBand = TownReservePressureBand.Stable;
            }

            ResolveTopTownReserveCategory(ref snapshot, categoryHouseholds, categoryNames, tracked);
            BuildTownReservePressureCopy(ref snapshot);
            return snapshot;
        }

        private static void ResolveTopTownReserveCategory(
            ref TownReservePressureSnapshot snapshot,
            Dictionary<string, int> categoryHouseholds,
            Dictionary<string, string> categoryNames,
            int trackedHouseholds)
        {
            string topId = string.Empty;
            string topName = string.Empty;
            int topCount = 0;
            foreach (KeyValuePair<string, int> entry in categoryHouseholds)
            {
                if (entry.Value <= topCount)
                {
                    continue;
                }

                topId = entry.Key;
                topCount = entry.Value;
                topName = categoryNames.TryGetValue(entry.Key, out string displayName) ? displayName : entry.Key;
            }

            snapshot.topCategoryId = topId;
            snapshot.topPressureCategoryId = topId;
            snapshot.topCategoryName = topName;
            snapshot.topPressureCategoryName = topName;
            snapshot.topCategoryHouseholds = topCount;
            snapshot.topCategoryPressure01 = trackedHouseholds <= 0 ? 0f : Mathf.Clamp01(topCount / (float)trackedHouseholds);
        }

        private static void BuildTownReservePressureCopy(ref TownReservePressureSnapshot snapshot)
        {
            string category = string.IsNullOrWhiteSpace(snapshot.topCategoryName) ? "household reserves" : snapshot.topCategoryName;
            switch (snapshot.pressureBand)
            {
                case TownReservePressureBand.Urgent:
                    snapshot.headline = $"Reserve pressure urgent: {category}.";
                    snapshot.primaryCause = "Multiple households are carrying critical reserves or remembered stockouts.";
                    snapshot.recommendedAction = BuildTownReserveAction(snapshot, "Prioritize emergency restock and supplier reliability before household strain turns into durable preference loss.");
                    break;
                case TownReservePressureBand.Pressure:
                    snapshot.headline = $"Reserve pressure rising: {category}.";
                    snapshot.primaryCause = "Household reserve strain is spreading beyond isolated cases.";
                    snapshot.recommendedAction = BuildTownReserveAction(snapshot, "Check local stock coverage and stabilize the most repeated household shortage.");
                    break;
                case TownReservePressureBand.Watch:
                    snapshot.headline = $"Reserve pressure watch: {category}.";
                    snapshot.primaryCause = "Some households are near low reserve thresholds or still remember recent shortfalls.";
                    snapshot.recommendedAction = BuildTownReserveAction(snapshot, "Monitor household shopping needs and address the first repeated shortage before it escalates.");
                    break;
                default:
                    snapshot.headline = "Town household reserves are stable.";
                    snapshot.primaryCause = "No broad household reserve pressure is currently visible.";
                    snapshot.recommendedAction = string.Empty;
                    break;
            }
        }

        private static string BuildTownReserveAction(TownReservePressureSnapshot snapshot, string fallback)
        {
            if (snapshot.fuelPressureHouseholds > 0
                && snapshot.fuelPressureHouseholds >= snapshot.foodPressureHouseholds
                && snapshot.fuelPressureHouseholds >= snapshot.medicinePressureHouseholds)
            {
                return "Prioritize fuel supply before heating strain compounds illness risk, wage loss, and emergency purchases.";
            }

            if (snapshot.medicinePressureHouseholds > 0
                && snapshot.medicinePressureHouseholds >= snapshot.foodPressureHouseholds)
            {
                return "Prioritize remedies and doctor access before health burden spreads through households and workplaces.";
            }

            if (snapshot.foodPressureHouseholds > 0)
            {
                return "Prioritize staple food coverage before household frustration and store preference penalties build.";
            }

            if (snapshot.stockoutMemoryHouseholds > 0)
            {
                return "Improve local supply reliability before remembered stockouts become a durable trust penalty.";
            }

            return fallback;
        }

        private static string BuildTownReserveSummary(TownReservePressureSnapshot snapshot)
        {
            if (!snapshot.initialized)
            {
                return "Town reserve pressure has not been calculated.";
            }

            StringBuilder builder = new();
            builder.Append(snapshot.headline);
            builder.Append(" Tracked households: ");
            builder.Append(snapshot.trackedHouseholds);
            builder.Append(". Low: ");
            builder.Append(snapshot.lowReserveHouseholds);
            builder.Append(", critical: ");
            builder.Append(snapshot.criticalReserveHouseholds);
            builder.Append(", stockout memory: ");
            builder.Append(snapshot.stockoutMemoryHouseholds);
            builder.Append('.');
            if (!string.IsNullOrWhiteSpace(snapshot.recommendedAction))
            {
                builder.Append(" Action: ");
                builder.Append(snapshot.recommendedAction);
            }

            return builder.ToString();
        }

        private void RefreshSummaryFields(string summary)
        {
            generatedHouseholdCount = state != null && state.households != null ? state.households.Count : 0;
            generatedPersonCount = state != null && state.people != null ? state.people.Count : 0;
            assignedWorkerCount = 0;
            totalWeeklyWages = 0;

            if (state != null && state.people != null)
            {
                for (int i = 0; i < state.people.Count; i++)
                {
                    PersonState person = state.people[i];
                    if (person == null)
                    {
                        continue;
                    }

                    if (person.workplaceBuildingId >= 0)
                    {
                        assignedWorkerCount++;
                    }

                    HouseholdState household = state.GetHousehold(person.householdId);
                    totalWeeklyWages += PopulationHealthEvaluator.GetEffectiveWeeklyWageDollars(person, household, state);
                }
            }

            if (state != null)
            {
                RefreshTownReservePressure();
            }

            lastGenerationSummary = string.IsNullOrWhiteSpace(summary)
                ? $"Households={generatedHouseholdCount}, People={generatedPersonCount}, Workers={assignedWorkerCount}, WeeklyWages=${totalWeeklyWages}"
                : summary;
        }

        private string BuildValidationLog()
        {
            if (state == null || state.validationMessages == null || state.validationMessages.Count == 0)
            {
                return "Population validation passed.";
            }

            StringBuilder builder = new();
            builder.AppendLine("Population validation:");
            for (int i = 0; i < state.validationMessages.Count; i++)
            {
                builder.AppendLine(state.validationMessages[i]);
            }

            return builder.ToString();
        }

        private string BuildHouseholdPreview()
        {
            if (state == null || state.households == null || state.households.Count == 0)
            {
                return "No generated households.";
            }

            StringBuilder builder = new();
            builder.AppendLine("Household preview:");
            int count = Mathf.Min(previewHouseholdLimit, state.households.Count);
            for (int i = 0; i < count; i++)
            {
                HouseholdState household = state.households[i];
                if (household == null)
                {
                    continue;
                }

                household.EnsureHouseholdReservesInitialized();
                household.EnsureHouseholdUpgradesInitialized();

                builder.Append(" - ");
                builder.AppendLine(BuildHouseholdInspectionSummary(household.id));
                builder.Append("   Reserves: ");
                builder.AppendLine(household.BuildReserveSummary());
                builder.Append("   Heat: ");
                builder.AppendLine(household.BuildHeatingSummary());
                builder.Append("   Settlement: ");
                builder.AppendLine(household.BuildSettlementSummaryLine());
                builder.Append("   Upgrades: ");
                builder.AppendLine(household.BuildUpgradeSummary());
            }

            return builder.ToString();
        }

        private string BuildHouseholdHealthInspectionSegment(HouseholdState household)
        {
            if (household == null || state == null || household.memberIds == null)
            {
                return string.Empty;
            }

            int activeCases = 0;
            int seriousCases = 0;
            for (int i = 0; i < household.memberIds.Count; i++)
            {
                PersonState person = state.GetPerson(household.memberIds[i]);
                PersonHealthState health = PopulationHealthEvaluator.EnsureHealthInitialized(person);
                if (health == null || !health.HasActiveCondition)
                {
                    continue;
                }

                activeCases++;
                if (health.severity == HealthConditionSeverity.Serious)
                {
                    seriousCases++;
                }
            }

            if (activeCases <= 0 && household.lastWeeklyLaborLossCents <= 0 && household.lastDailyMedicalSpendCents <= 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            builder.Append("Health ");
            builder.Append(activeCases);
            builder.Append(" active");
            if (seriousCases > 0)
            {
                builder.Append(" (");
                builder.Append(seriousCases);
                builder.Append(" serious)");
            }

            if (household.lastWeeklyLaborLossCents > 0)
            {
                builder.Append(" | labor loss ");
                builder.Append(FormatMoney(household.lastWeeklyLaborLossCents));
            }

            if (household.lastDailyMedicalSpendCents > 0)
            {
                builder.Append(" | medical spend ");
                builder.Append(FormatMoney(household.lastDailyMedicalSpendCents));
            }

            return builder.ToString();
        }
    }
}
