using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    public sealed class PopulationHealthEconomyTests
    {
        [Test]
        public void LegacyPopulationLoadDefaultsToHealthyAndZeroHealthEconomics()
        {
            GameObject targetObject = new("Health Legacy Load Target");
            try
            {
                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                PopulationSaveDto dto = new();
                dto.people.Add(CreatePersonDto(1, 7, 20, null));
                dto.households.Add(new HouseholdSaveDto
                {
                    id = 7,
                    householdName = "Legacy Household",
                    surname = "Legacy",
                    homeBuildingId = 3,
                    memberIds = new List<int> { 1 }
                });

                target.LoadFromSaveDto(dto);

                PersonState person = target.State.GetPerson(1);
                HouseholdState household = target.State.GetHousehold(7);
                Assert.NotNull(person.health);
                Assert.IsFalse(person.health.HasActiveCondition);
                Assert.AreEqual(100, person.health.laborCapacityPercent);
                Assert.AreEqual(20, household.weeklyIncomeSnapshot);
                Assert.AreEqual(0, household.lastDailyMedicalSpendCents);
                Assert.AreEqual(0, household.lastWeeklyLaborLossCents);
                Assert.AreEqual(0, household.lifetimeMedicalSpendCents);
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PopulationSaveLoadRoundTripsHealthAndMedicalLaborFields()
        {
            GameObject sourceObject = new("Health Save Source");
            GameObject targetObject = new("Health Save Target");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                PersonState worker = CreatePerson(2, 8, 20);
                worker.health.ApplyCondition(HealthConditionKind.Illness, HealthConditionSeverity.Serious, 4, "Test illness.");
                HouseholdState household = CreateHousehold(8, worker.id);
                household.lastDailyMedicalSpendCents = 125;
                household.lastWeeklyLaborLossCents = 1500;
                household.lifetimeMedicalSpendCents = 500;
                source.State.people.Add(worker);
                source.State.households.Add(household);
                source.RecalculateHouseholdIncomeAndSummaries("Health save source.");

                PopulationSaveDto dto = source.CaptureSaveDto();
                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                PersonState restoredWorker = target.State.GetPerson(worker.id);
                HouseholdState restoredHousehold = target.State.GetHousehold(household.id);
                Assert.AreEqual(HealthConditionKind.Illness, restoredWorker.health.conditionKind);
                Assert.AreEqual(HealthConditionSeverity.Serious, restoredWorker.health.severity);
                Assert.AreEqual(4, restoredWorker.health.remainingDays);
                Assert.AreEqual(35, restoredWorker.health.laborCapacityPercent);
                Assert.AreEqual(125, restoredHousehold.lastDailyMedicalSpendCents);
                Assert.AreEqual(1500, restoredHousehold.lastWeeklyLaborLossCents);
                Assert.AreEqual(500, restoredHousehold.lifetimeMedicalSpendCents);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PopulationSaveLoadRoundTripsNewcomerSettlementAndBoardingFields()
        {
            GameObject sourceObject = new("Settlement Save Source");
            GameObject targetObject = new("Settlement Save Target");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();

                PersonState host = CreatePerson(41, 88, 30);
                PersonState boarder = CreatePerson(42, 88, 18);
                boarder.arrivalProfile = NewcomerArrivalProfile.BoarderSeekingWorker;
                boarder.settlementArrangement = SettlementArrangement.Boarding;
                boarder.settlementPressure = SettlementPressureState.Create(SettlementArrangement.Boarding, 0.48f, 0.62f, 0.57f, 3, 0.72f, "Boarding test pressure");
                boarder.startingCashCents = 1750;
                boarder.laborUrgency01 = 0.81f;
                boarder.laborReadinessModifier = 12;
                boarder.preferredProfessionBias = "trade";
                boarder.settlementDifficulty = 4;
                boarder.hostHouseholdId = 88;

                PersonState renter = CreatePerson(43, 89, 24);
                renter.arrivalProfile = NewcomerArrivalProfile.RenterReadyHousehold;
                renter.settlementArrangement = SettlementArrangement.Renting;

                PersonState transient = CreatePerson(44, 90, 12);
                transient.arrivalProfile = NewcomerArrivalProfile.LoneLaborer;
                transient.settlementArrangement = SettlementArrangement.Transient;
                transient.hostHouseholdId = -1;

                HouseholdState hostHousehold = CreateHousehold(88, host.id);
                hostHousehold.arrivalProfile = NewcomerArrivalProfile.KinLinkedArrival;
                hostHousehold.settlementArrangement = SettlementArrangement.Boarding;
                hostHousehold.settlementPressure = SettlementPressureState.Create(SettlementArrangement.Boarding, 0.51f, 0.45f, 0.59f, 2, 0.61f, "Host boarding pressure");
                hostHousehold.settlementReserveStrengthCents = 6200;
                hostHousehold.baseBoardingCapacity = 2;
                hostHousehold.boardingCapacity = 2;
                hostHousehold.boarderPersonIds = new List<int> { boarder.id };
                hostHousehold.hostsBoarders = true;
                hostHousehold.hasKinAbsorptionPressure = true;

                HouseholdState renterHousehold = CreateHousehold(89, renter.id);
                renterHousehold.arrivalProfile = NewcomerArrivalProfile.RenterReadyHousehold;
                renterHousehold.settlementArrangement = SettlementArrangement.Renting;
                renterHousehold.isRenterHousehold = true;

                HouseholdState transientHousehold = CreateHousehold(90, transient.id);
                transientHousehold.arrivalProfile = NewcomerArrivalProfile.LoneLaborer;
                transientHousehold.settlementArrangement = SettlementArrangement.Transient;
                transientHousehold.isTransientHousehold = true;

                source.State.people.Add(host);
                source.State.people.Add(boarder);
                source.State.people.Add(renter);
                source.State.people.Add(transient);
                source.State.households.Add(hostHousehold);
                source.State.households.Add(renterHousehold);
                source.State.households.Add(transientHousehold);

                PopulationSaveDto dto = source.CaptureSaveDto();
                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                PersonState restoredBoarder = target.State.GetPerson(boarder.id);
                Assert.AreEqual(NewcomerArrivalProfile.BoarderSeekingWorker, restoredBoarder.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.Boarding, restoredBoarder.settlementArrangement);
                Assert.AreEqual(1750, restoredBoarder.startingCashCents);
                Assert.AreEqual(0.81f, restoredBoarder.laborUrgency01, 0.0001f);
                Assert.AreEqual(12, restoredBoarder.laborReadinessModifier);
                Assert.AreEqual("trade", restoredBoarder.preferredProfessionBias);
                Assert.AreEqual(4, restoredBoarder.settlementDifficulty);
                Assert.AreEqual(88, restoredBoarder.hostHouseholdId);
                Assert.AreEqual(3, restoredBoarder.settlementPressure.weeksInArrangement);

                PersonState restoredRenter = target.State.GetPerson(renter.id);
                Assert.AreEqual(NewcomerArrivalProfile.RenterReadyHousehold, restoredRenter.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.Renting, restoredRenter.settlementArrangement);

                PersonState restoredTransient = target.State.GetPerson(transient.id);
                Assert.AreEqual(NewcomerArrivalProfile.LoneLaborer, restoredTransient.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.Transient, restoredTransient.settlementArrangement);

                HouseholdState restoredHostHousehold = target.State.GetHousehold(hostHousehold.id);
                Assert.AreEqual(NewcomerArrivalProfile.KinLinkedArrival, restoredHostHousehold.arrivalProfile);
                Assert.AreEqual(SettlementArrangement.Boarding, restoredHostHousehold.settlementArrangement);
                Assert.AreEqual(6200, restoredHostHousehold.settlementReserveStrengthCents);
                Assert.AreEqual(2, restoredHostHousehold.baseBoardingCapacity);
                Assert.AreEqual(2, restoredHostHousehold.boardingCapacity);
                CollectionAssert.AreEqual(new List<int> { boarder.id }, restoredHostHousehold.boarderPersonIds);
                Assert.IsTrue(restoredHostHousehold.hostsBoarders);
                Assert.IsTrue(restoredHostHousehold.hasKinAbsorptionPressure);
                Assert.AreEqual(2, restoredHostHousehold.settlementPressure.weeksInArrangement);

                HouseholdState restoredRenterHousehold = target.State.GetHousehold(renterHousehold.id);
                Assert.AreEqual(SettlementArrangement.Renting, restoredRenterHousehold.settlementArrangement);
                Assert.IsTrue(restoredRenterHousehold.isRenterHousehold);

                HouseholdState restoredTransientHousehold = target.State.GetHousehold(transientHousehold.id);
                Assert.AreEqual(SettlementArrangement.Transient, restoredTransientHousehold.settlementArrangement);
                Assert.IsTrue(restoredTransientHousehold.isTransientHousehold);
                Assert.AreEqual(2, target.State.boardingCapacity);
                Assert.AreEqual(1, target.State.boardingUsed);
                Assert.AreEqual(1, target.State.renterHouseholdCount);
                Assert.AreEqual(1, target.State.transientPersonCount);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void SickOrInjuredWorkerReducesHouseholdIncomeAndRecordsLaborLoss()
        {
            GameObject populationObject = new("Health Labor Loss Population");
            try
            {
                PopulationManager population = populationObject.AddComponent<PopulationManager>();
                PersonState worker = CreatePerson(3, 9, 20);
                worker.health.ApplyCondition(HealthConditionKind.Injury, HealthConditionSeverity.Mild, 3, "Test injury.");
                HouseholdState household = CreateHousehold(9, worker.id);
                population.State.people.Add(worker);
                population.State.households.Add(household);

                population.RecalculateHouseholdIncomeAndSummaries("Health labor loss.");

                Assert.AreEqual(10, household.weeklyIncomeSnapshot);
                Assert.AreEqual(1000, household.lastWeeklyLaborLossCents);
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
            }
        }

        [Test]
        public void DailyHealthResolutionIsIdempotentForSameDayUnlessForced()
        {
            GameObject populationObject = new("Health Idempotence Population");
            try
            {
                PopulationManager population = populationObject.AddComponent<PopulationManager>();
                PersonState worker = CreatePerson(4, 10, 20);
                worker.health.ApplyCondition(HealthConditionKind.Injury, HealthConditionSeverity.Mild, 3, "Test injury.");
                population.State.people.Add(worker);
                population.State.households.Add(CreateHousehold(10, worker.id));

                population.ResolveDailyHealth(12, true);
                Assert.AreEqual(2, worker.health.remainingDays);

                population.ResolveDailyHealth(12);
                Assert.AreEqual(2, worker.health.remainingDays);

                population.ResolveDailyHealth(12, true);
                Assert.AreEqual(1, worker.health.remainingDays);
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
            }
        }

        [Test]
        public void DoctorTreatmentChargesHouseholdAddsRevenueAndConsumesIllnessRemedy()
        {
            GameObject populationObject = new("Doctor Treatment Population");
            GameObject runtimeObject = new("Doctor Treatment Runtime");
            try
            {
                PopulationManager population = populationObject.AddComponent<PopulationManager>();
                PersonState patient = CreatePerson(5, 11, 20);
                patient.health.ApplyCondition(HealthConditionKind.Illness, HealthConditionSeverity.Serious, 4, "Test illness.");
                HouseholdState household = CreateHousehold(11, patient.id);
                household.spendingMoneyCents = 10000;
                population.State.people.Add(patient);
                population.State.households.Add(household);

                BusinessProfileDefinition doctorProfile = LoadProfile(BusinessType.Doctor);
                BusinessInstanceState doctor = BusinessInstanceState.Create(
                    "test_doctor",
                    doctorProfile,
                    1,
                    BusinessOwnerIdentity.Npc(1005, "Samuel Reed", "Reed"));
                doctor.EnsureOwnerOperatorStaffing(doctor.Owner);
                CategoryStockState remedies = doctor.RuntimeState.GetCategoryStock("medicine_remedies");
                Assert.NotNull(remedies);
                remedies.SetCurrentStockForTests(2);

                SharedBusinessRuntimeManager runtime = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                runtime.Configure(null, new[] { doctorProfile }, population);
                SetBusinesses(runtime, new List<BusinessInstanceState> { doctor });
                int cashBefore = doctor.RuntimeState.CurrentCashCents;
                int householdMoneyBefore = household.spendingMoneyCents;
                int remedyStockBefore = remedies.CurrentStockUnits;

                PopulationHealthDailySnapshot snapshot = runtime.ResolveDailyDoctorTreatments(population.State, 22);

                Assert.AreEqual(1, snapshot.treatedCases);
                Assert.AreEqual(0, snapshot.untreatedCases);
                Assert.AreEqual(2, patient.health.remainingDays);
                Assert.AreEqual(22, patient.health.lastTreatedDayIndex);
                Assert.AreEqual(1, doctor.LastDailyServiceVisits);
                Assert.AreEqual(remedyStockBefore - 1, remedies.CurrentStockUnits);
                Assert.Less(household.spendingMoneyCents, householdMoneyBefore);
                Assert.Greater(doctor.RuntimeState.CurrentCashCents, cashBefore);
                Assert.AreEqual(snapshot.medicalSpendCents, snapshot.doctorRevenueCents);
                Assert.AreEqual(snapshot.medicalSpendCents, household.lastDailyMedicalSpendCents);
                Assert.AreEqual(snapshot.medicalSpendCents, household.lifetimeMedicalSpendCents);
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void NoActiveDoctorLeavesCasesUntreatedAndLaborLossRemains()
        {
            GameObject populationObject = new("No Doctor Health Population");
            GameObject runtimeObject = new("No Doctor Runtime");
            try
            {
                PopulationManager population = populationObject.AddComponent<PopulationManager>();
                PersonState worker = CreatePerson(6, 12, 20);
                worker.health.ApplyCondition(HealthConditionKind.Illness, HealthConditionSeverity.Mild, 3, "Test illness.");
                HouseholdState household = CreateHousehold(12, worker.id);
                population.State.people.Add(worker);
                population.State.households.Add(household);

                SharedBusinessRuntimeManager runtime = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                runtime.Configure(null, new List<BusinessProfileDefinition>(), population);
                SetPrivateField(population, "sharedBusinessRuntime", runtime);

                PopulationHealthDailySnapshot snapshot = population.ResolveDailyHealth(30, true);

                Assert.AreEqual(1, snapshot.activeCases);
                Assert.AreEqual(0, snapshot.treatedCases);
                Assert.AreEqual(1, snapshot.untreatedCases);
                Assert.AreEqual(600, household.lastWeeklyLaborLossCents);
                Assert.AreEqual(14, household.weeklyIncomeSnapshot);
                Assert.AreEqual(0, household.lastDailyMedicalSpendCents);
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void HealthAdjustedWorkerAvailabilityReducesSharedBusinessCapacity()
        {
            GameObject populationObject = new("Health Capacity Population");
            GameObject runtimeObject = new("Health Capacity Runtime");
            try
            {
                PopulationManager population = populationObject.AddComponent<PopulationManager>();
                PersonState worker = CreatePerson(7, 13, 20);
                worker.health.ApplyCondition(HealthConditionKind.Injury, HealthConditionSeverity.Serious, 6, "Test injury.");
                population.State.people.Add(worker);
                population.State.households.Add(CreateHousehold(13, worker.id));

                BusinessProfileDefinition blacksmithProfile = LoadProfile(BusinessType.Blacksmith);
                BusinessInstanceState blacksmith = BusinessInstanceState.Create(
                    "test_blacksmith",
                    blacksmithProfile,
                    2,
                    BusinessOwnerIdentity.Player());
                WorkerSlotState requiredSlot = FindRequiredSlot(blacksmith);
                requiredSlot.Assign(worker.id.ToString(), worker.DisplayName, requiredSlot.WeeklyWageCents);

                SharedBusinessRuntimeManager runtime = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                runtime.Configure(null, new[] { blacksmithProfile }, population);

                Assert.AreEqual(0.15f, runtime.GetHealthAdjustedOperatingEfficiency01(blacksmith), 0.0001f);

                worker.health.ApplyTreatment(33, 6, "Recovered for test.");
                Assert.Greater(runtime.GetHealthAdjustedOperatingEfficiency01(blacksmith), 0f);
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void HouseholdHealthStrainReducesHealthyMemberIncomeAndShowsInspectionHealthPressure()
        {
            GameObject populationObject = new("Household Health Strain Population");
            try
            {
                PopulationManager population = populationObject.AddComponent<PopulationManager>();
                PersonState healthyWorker = CreatePerson(14, 20, 20);
                PersonState illWorker = CreatePerson(15, 20, 20);
                illWorker.health.ApplyCondition(HealthConditionKind.Illness, HealthConditionSeverity.Serious, 4, "Household strain test.");

                HouseholdState household = CreateHousehold(20, healthyWorker.id, illWorker.id);
                population.State.people.Add(healthyWorker);
                population.State.people.Add(illWorker);
                population.State.households.Add(household);

                population.RecalculateHouseholdIncomeAndSummaries("Household health strain.");

                Assert.AreEqual(23, household.weeklyIncomeSnapshot);
                Assert.AreEqual(1700, household.lastWeeklyLaborLossCents);

                string inspection = population.BuildHouseholdInspectionSummary(household.id);
                StringAssert.Contains("Health 1 active", inspection);
                StringAssert.Contains("labor loss $17.00", inspection);
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
            }
        }

        [Test]
        public void InjuryExposureByProfessionKeepsForgeWorkRiskierThanDoctoring()
        {
            PersonState blacksmith = CreatePerson(16, 21, 20);
            blacksmith.professionId = "blacksmith_striker";
            blacksmith.professionName = "Blacksmith Striker";
            blacksmith.hiddenWorkerTraits = WorkerHiddenTraits.Create(70, 60, 50, 60, 55, 55);

            PersonState doctor = CreatePerson(17, 22, 20);
            doctor.professionId = "doctor_physician";
            doctor.professionName = "Town Physician";
            doctor.hiddenWorkerTraits = WorkerHiddenTraits.Create(70, 60, 50, 60, 55, 55);

            Assert.Greater(
                PopulationHealthEvaluator.GetInjuryRiskPerTenThousand(blacksmith),
                PopulationHealthEvaluator.GetInjuryRiskPerTenThousand(doctor));
        }

        [Test]
        public void DoctorTreatmentPrioritizesSeriousLaborCasesAndStepsSeverityDown()
        {
            GameObject populationObject = new("Doctor Triage Population");
            GameObject runtimeObject = new("Doctor Triage Runtime");
            try
            {
                PopulationManager population = populationObject.AddComponent<PopulationManager>();

                PersonState doctorWorker = CreatePerson(18, 23, 22);
                doctorWorker.health.ApplyCondition(HealthConditionKind.Injury, HealthConditionSeverity.Serious, 6, "Doctor capacity reduced for triage test.");
                doctorWorker.workplaceBuildingId = 90;

                PersonState seriousPatient = CreatePerson(19, 24, 20);
                seriousPatient.health.ApplyCondition(HealthConditionKind.Illness, HealthConditionSeverity.Serious, 4, "Serious patient.");

                PersonState mildPatient = CreatePerson(20, 25, 20);
                mildPatient.health.ApplyCondition(HealthConditionKind.Illness, HealthConditionSeverity.Mild, 2, "Mild patient.");

                HouseholdState seriousHousehold = CreateHousehold(24, seriousPatient.id);
                HouseholdState mildHousehold = CreateHousehold(25, mildPatient.id);
                seriousHousehold.spendingMoneyCents = 10000;
                mildHousehold.spendingMoneyCents = 10000;

                population.State.people.Add(doctorWorker);
                population.State.people.Add(seriousPatient);
                population.State.people.Add(mildPatient);
                population.State.households.Add(CreateHousehold(23, doctorWorker.id));
                population.State.households.Add(seriousHousehold);
                population.State.households.Add(mildHousehold);

                BusinessProfileDefinition doctorProfile = LoadProfile(BusinessType.Doctor);
                BusinessInstanceState doctor = BusinessInstanceState.Create(
                    "triage_doctor",
                    doctorProfile,
                    90,
                    BusinessOwnerIdentity.Npc(1018, "Elias Crane", "Crane"));

                WorkerSlotState physician = FindRequiredSlot(doctor);
                physician.Assign(doctorWorker.id.ToString(), doctorWorker.DisplayName, physician.WeeklyWageCents);
                CategoryStockState remedies = doctor.RuntimeState.GetCategoryStock("medicine_remedies");
                remedies.SetCurrentStockForTests(2);

                SharedBusinessRuntimeManager runtime = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                runtime.Configure(null, new[] { doctorProfile }, population);
                SetBusinesses(runtime, new List<BusinessInstanceState> { doctor });

                PopulationHealthDailySnapshot snapshot = runtime.ResolveDailyDoctorTreatments(population.State, 41);

                Assert.AreEqual(1, snapshot.treatedCases);
                Assert.AreEqual(1, snapshot.untreatedCases);
                Assert.AreEqual(HealthConditionSeverity.Mild, seriousPatient.health.severity);
                Assert.AreEqual(41, seriousPatient.health.lastTreatedDayIndex);
                Assert.AreEqual(2, seriousPatient.health.remainingDays);
                Assert.AreEqual(HealthConditionSeverity.Mild, mildPatient.health.severity);
                Assert.AreEqual(-1, mildPatient.health.lastTreatedDayIndex);
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void ManagementTextCallsOutHealthStaffingPressure()
        {
            GameObject populationObject = new("Management Health Population");
            GameObject runtimeObject = new("Management Health Runtime");
            try
            {
                PopulationManager population = populationObject.AddComponent<PopulationManager>();
                PersonState worker = CreatePerson(21, 26, 20);
                worker.professionId = "blacksmith_striker";
                worker.professionName = "Blacksmith Striker";
                worker.health.ApplyCondition(HealthConditionKind.Injury, HealthConditionSeverity.Serious, 5, "Management text test.");
                population.State.people.Add(worker);
                population.State.households.Add(CreateHousehold(26, worker.id));

                BusinessProfileDefinition blacksmithProfile = LoadProfile(BusinessType.Blacksmith);
                BusinessInstanceState blacksmith = BusinessInstanceState.Create(
                    "health_text_blacksmith",
                    blacksmithProfile,
                    44,
                    BusinessOwnerIdentity.Player());

                WorkerSlotState slot = FindRequiredSlot(blacksmith);
                slot.Assign(worker.id.ToString(), worker.DisplayName, slot.WeeklyWageCents);

                SharedBusinessRuntimeManager runtime = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                runtime.Configure(null, new[] { blacksmithProfile }, population);

                string text = runtime.BuildManagementText(blacksmith, 0);

                StringAssert.Contains("Health staffing:", text);
                StringAssert.Contains("serious injury", text.ToLowerInvariant());
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
                Object.DestroyImmediate(runtimeObject);
            }
        }

        private static PersonState CreatePerson(int id, int householdId, int weeklyWage)
        {
            return new PersonState
            {
                id = id,
                firstName = $"Test{id}",
                lastName = "Worker",
                age = 28,
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
                professionId = "test_worker",
                professionName = "Test Worker",
                wage = new WageSnapshot
                {
                    weeklyWage = weeklyWage,
                    stabilityPercent = 80,
                    currencyId = "dollars"
                },
                health = new PersonHealthState(),
                homeBuildingId = 1,
                workplaceBuildingId = 2,
                scheduleState = PopulationScheduleState.AtHome,
                currentDestinationBuildingId = 1
            };
        }

        private static PersonSaveDto CreatePersonDto(int id, int householdId, int weeklyWage, PersonHealthState health)
        {
            return new PersonSaveDto
            {
                id = id,
                firstName = $"Saved{id}",
                lastName = "Worker",
                age = 28,
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
                professionId = "saved_worker",
                professionName = "Saved Worker",
                wage = new WageSnapshot
                {
                    weeklyWage = weeklyWage,
                    stabilityPercent = 80,
                    currencyId = "dollars"
                },
                health = health,
                homeBuildingId = 1,
                workplaceBuildingId = 2,
                scheduleState = PopulationScheduleState.AtHome,
                currentDestinationBuildingId = 1
            };
        }

        private static HouseholdState CreateHousehold(int householdId, params int[] memberIds)
        {
            return new HouseholdState
            {
                id = householdId,
                householdName = $"Household {householdId}",
                surname = "Test",
                homeBuildingId = 1,
                spendingMoneyCents = 10000,
                memberIds = memberIds != null ? new List<int>(memberIds) : new List<int>()
            };
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

            Assert.Fail($"Expected {businessType} profile.");
            return null;
        }

        private static WorkerSlotState FindRequiredSlot(BusinessInstanceState business)
        {
            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = business.RuntimeState.WorkerSlots[i];
                if (slot.RequiredForOpening)
                {
                    return slot;
                }
            }

            Assert.Fail($"{business.BusinessType} should have a required worker slot.");
            return null;
        }

        private static void SetBusinesses(SharedBusinessRuntimeManager manager, List<BusinessInstanceState> businesses)
        {
            SetPrivateField(manager, "businesses", businesses);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"Field {fieldName} should exist on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    

        [Test]
        public void HealthStateSummariesDescribeConditionAndPressure()
        {
            PersonHealthState health = new()
            {
                conditionKind = HealthConditionKind.Illness,
                severity = HealthConditionSeverity.Serious,
                remainingDays = 3,
                laborCapacityPercent = 40,
                lastTreatedDayIndex = 12
            };

            StringAssert.Contains("Serious Illness", health.BuildConditionSummary());
            StringAssert.Contains("day 12", health.BuildTreatmentReadSummary().ToLowerInvariant());

            PopulationHealthDailySnapshot snapshot = new()
            {
                activeCases = 4,
                untreatedCases = 2,
                treatedCases = 2,
                summary = "Doctor under strain."
            };

            StringAssert.Contains("Health pressure rising", snapshot.BuildPressureHeadline());
            StringAssert.Contains("Doctor under strain", snapshot.BuildDailySummary());
        }

    }
}
