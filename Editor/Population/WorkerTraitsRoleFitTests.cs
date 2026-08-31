using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.MVP;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Population
{
    public sealed class WorkerTraitsRoleFitTests
    {
        [Test]
        public void PersonInitializesWorkerTraitsWhenMissing()
        {
            PersonState person = CreatePerson(1);
            person.hiddenWorkerTraits = null;
            person.visibleWorkerProfile = null;
            person.workerTraitVisibility = null;

            person.EnsureWorkerTraitsInitialized(42);

            Assert.IsTrue(person.hiddenWorkerTraits.IsInitialized);
            AssertTraitRange(person.hiddenWorkerTraits.reliability);
            AssertTraitRange(person.hiddenWorkerTraits.care);
            AssertTraitRange(person.hiddenWorkerTraits.socialSkill);
            AssertTraitRange(person.hiddenWorkerTraits.honesty);
            AssertTraitRange(person.hiddenWorkerTraits.learningSpeed);
            AssertTraitRange(person.hiddenWorkerTraits.durability);
            Assert.IsTrue(person.visibleWorkerProfile.IsInitialized);
            Assert.IsFalse(string.IsNullOrWhiteSpace(person.visibleWorkerProfile.roleHistorySummary));
            Assert.IsTrue(person.workerTraitVisibility.IsInitialized);
            Assert.IsTrue(person.workerTraitVisibility.roleFitHintVisible);
        }

        [Test]
        public void PopulationSaveLoadRoundTripsWorkerTraits()
        {
            GameObject sourceObject = new("PopulationManager_Source");
            GameObject loadedObject = new("PopulationManager_Loaded");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                PopulationManager loaded = loadedObject.AddComponent<PopulationManager>();
                PersonState original = CreatePerson(
                    7,
                    LaborAccessLevel.FullLaborMarket,
                    -1,
                    WorkerHiddenTraits.Create(81, 73, 64, 88, 59, 67));
                original.visibleWorkerProfile = WorkerVisibleProfile.Create(62, 1550, "Worked as Stock Helper");
                original.workerTraitVisibility = WorkerTraitVisibility.FirstPassDefault();

                PopulationState state = new();
                state.people.Add(original);
                SetPrivateField(source, "state", state);

                LandLedgers.Persistence.PopulationSaveDto dto = source.CaptureSaveDto();
                loaded.LoadFromSaveDto(dto);

                PersonState roundTripped = loaded.State.GetPerson(7);
                Assert.NotNull(roundTripped);
                Assert.AreEqual(81, roundTripped.hiddenWorkerTraits.reliability);
                Assert.AreEqual(73, roundTripped.hiddenWorkerTraits.care);
                Assert.AreEqual(64, roundTripped.hiddenWorkerTraits.socialSkill);
                Assert.AreEqual(88, roundTripped.hiddenWorkerTraits.honesty);
                Assert.AreEqual(59, roundTripped.hiddenWorkerTraits.learningSpeed);
                Assert.AreEqual(67, roundTripped.hiddenWorkerTraits.durability);
                Assert.AreEqual(62, roundTripped.visibleWorkerProfile.generalSkill);
                Assert.AreEqual(1550, roundTripped.visibleWorkerProfile.wageExpectationCents);
                Assert.AreEqual("Worked as Stock Helper", roundTripped.visibleWorkerProfile.roleHistorySummary);
                Assert.IsTrue(roundTripped.workerTraitVisibility.roleFitHintVisible);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(loadedObject);
            }
        }

        [Test]
        public void PopulationLoadNormalizesTransientScheduleStates()
        {
            GameObject loadedObject = new("PopulationManager_ScheduleLoad");
            try
            {
                PopulationManager loaded = loadedObject.AddComponent<PopulationManager>();
                LandLedgers.Persistence.PopulationSaveDto dto = new();
                dto.people.Add(CreateScheduledPersonDto(30, PopulationScheduleState.GoingToWork, 10, 20, 20));
                dto.people.Add(CreateScheduledPersonDto(31, PopulationScheduleState.GoingShopping, 11, -1, 40));
                dto.people.Add(CreateScheduledPersonDto(32, PopulationScheduleState.ReturningHome, 12, 22, 22));

                loaded.LoadFromSaveDto(dto);

                Assert.AreEqual(PopulationScheduleState.AtWork, loaded.State.GetPerson(30).scheduleState);
                Assert.AreEqual(20, loaded.State.GetPerson(30).currentDestinationBuildingId);
                Assert.AreEqual(PopulationScheduleState.AtStore, loaded.State.GetPerson(31).scheduleState);
                Assert.AreEqual(40, loaded.State.GetPerson(31).currentDestinationBuildingId);
                Assert.AreEqual(PopulationScheduleState.AtHome, loaded.State.GetPerson(32).scheduleState);
                Assert.AreEqual(12, loaded.State.GetPerson(32).currentDestinationBuildingId);
            }
            finally
            {
                Object.DestroyImmediate(loadedObject);
            }
        }

        [Test]
        public void RuntimeScheduleNormalizationRecoversTransientStates()
        {
            GameObject populationObject = new("PopulationManager_ScheduleRuntime");
            try
            {
                PopulationManager populationManager = populationObject.AddComponent<PopulationManager>();
                PersonState worker = CreatePerson(33, workplaceBuildingId: 20);
                worker.scheduleState = PopulationScheduleState.GoingToWork;
                worker.currentDestinationBuildingId = 20;
                PersonState returning = CreatePerson(34, workplaceBuildingId: 21);
                returning.homeBuildingId = 11;
                returning.scheduleState = PopulationScheduleState.ReturningHome;
                returning.currentDestinationBuildingId = 21;
                populationManager.State.people.Add(worker);
                populationManager.State.people.Add(returning);

                int normalized = populationManager.NormalizeTransientScheduleStates();

                Assert.AreEqual(2, normalized);
                Assert.AreEqual(PopulationScheduleState.AtWork, populationManager.State.GetPerson(33).scheduleState);
                Assert.AreEqual(PopulationScheduleState.AtHome, populationManager.State.GetPerson(34).scheduleState);
                Assert.AreEqual(11, populationManager.State.GetPerson(34).currentDestinationBuildingId);
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
            }
        }

        [Test]
        public void GeneralStoreClerkPrefersSocialReliableCandidate()
        {
            PersonState strong = CreatePerson(10, traits: WorkerHiddenTraits.Create(86, 55, 90, 78, 50, 52));
            PersonState weak = CreatePerson(11, traits: WorkerHiddenTraits.Create(55, 55, 25, 50, 50, 52));

            WorkerRoleFitResult strongFit = WorkerRoleFitEvaluator.Evaluate(strong, BusinessType.GeneralStore, "clerk_helper", "Clerk / Helper");
            WorkerRoleFitResult weakFit = WorkerRoleFitEvaluator.Evaluate(weak, BusinessType.GeneralStore, "clerk_helper", "Clerk / Helper");

            Assert.Greater(strongFit.Score01, weakFit.Score01);
            Assert.AreEqual("Strong", strongFit.Label);
        }

        [Test]
        public void ButcherHelperPrefersCarefulReliableCandidate()
        {
            PersonState careful = CreatePerson(12, traits: WorkerHiddenTraits.Create(84, 92, 45, 70, 45, 64));
            PersonState careless = CreatePerson(13, traits: WorkerHiddenTraits.Create(50, 24, 45, 70, 45, 64));

            WorkerRoleFitResult carefulFit = WorkerRoleFitEvaluator.Evaluate(careful, BusinessType.Butcher, "counter_helper", "Counter Helper");
            WorkerRoleFitResult carelessFit = WorkerRoleFitEvaluator.Evaluate(careless, BusinessType.Butcher, "counter_helper", "Counter Helper");

            Assert.Greater(carefulFit.Score01, carelessFit.Score01);
        }

        [Test]
        public void BlacksmithHelperPrefersDurableFastLearner()
        {
            PersonState trainable = CreatePerson(14, traits: WorkerHiddenTraits.Create(78, 45, 45, 60, 88, 90));
            PersonState fragile = CreatePerson(15, traits: WorkerHiddenTraits.Create(78, 45, 45, 60, 25, 30));

            WorkerRoleFitResult trainableFit = WorkerRoleFitEvaluator.Evaluate(trainable, BusinessType.Blacksmith, "striker_helper", "Striker / Helper");
            WorkerRoleFitResult fragileFit = WorkerRoleFitEvaluator.Evaluate(fragile, BusinessType.Blacksmith, "striker_helper", "Striker / Helper");

            Assert.Greater(trainableFit.Score01, fragileFit.Score01);
        }

        [Test]
        public void ExistingHiringFiltersStillExcludeIneligibleAndEmployedPeople()
        {
            GameObject populationObject = new("PopulationManager_Filter");
            GameObject storeObject = new("GeneralStoreRuntimeManager_Filter");
            try
            {
                PopulationManager populationManager = populationObject.AddComponent<PopulationManager>();
                GeneralStoreRuntimeManager storeManager = storeObject.AddComponent<GeneralStoreRuntimeManager>();
                PopulationState state = new();
                state.people.Add(CreatePerson(20, LaborAccessLevel.YoungWorker));
                state.people.Add(CreatePerson(21, LaborAccessLevel.JuniorLowTrust));
                state.people.Add(CreatePerson(22, LaborAccessLevel.FullLaborMarket, 9));
                SetPrivateField(populationManager, "state", state);

                BusinessRuntimeState runtime = new();
                SetPrivateField(runtime, "workerSlots", new List<WorkerSlotState>
                {
                    new("clerk_helper", "Clerk / Helper", 1200, true)
                });
                SetPrivateField(storeManager, "populationManager", populationManager);
                SetPrivateField(storeManager, "runtimeState", runtime);

                MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                    "BuildAvailableWorkerCandidateList",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    System.Type.EmptyTypes,
                    null);
                Assert.NotNull(method);

                List<PersonState> candidates = (List<PersonState>)method.Invoke(storeManager, null);

                Assert.AreEqual(1, candidates.Count);
                Assert.AreEqual(20, candidates[0].id);
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void ApprenticeshipPromotesThroughSupportedPaidWeeks()
        {
            PersonState person = CreatePerson(40, traits: WorkerHiddenTraits.Create(88, 70, 80, 75, 92, 82));
            WorkerSlotState slot = new("clerk_helper", "Clerk / Helper", 1200, true);
            slot.Assign(person.id.ToString(), person.DisplayName, 1200);
            ApprenticeshipProgressionEvaluator.RecordAssignment(person, BusinessType.GeneralStore, 4, slot);

            for (int i = 0; i < 3; i++)
            {
                ApprenticeshipProgressionEvaluator.AdvancePaidWeek(person, BusinessType.GeneralStore, 4, slot);
            }

            Assert.AreEqual(ApprenticeshipStage.Apprentice, person.apprenticeship.Stage);
            Assert.GreaterOrEqual(person.apprenticeship.ExperiencePoints, ApprenticeshipProgressionEvaluator.HelperToApprenticeExperience);

            for (int i = 0; i < 6; i++)
            {
                ApprenticeshipProgressionEvaluator.AdvancePaidWeek(person, BusinessType.GeneralStore, 4, slot);
            }

            Assert.AreEqual(ApprenticeshipStage.Worker, person.apprenticeship.Stage);
            Assert.GreaterOrEqual(person.apprenticeship.ExperiencePoints, ApprenticeshipProgressionEvaluator.ApprenticeToWorkerExperience);
        }

        [Test]
        public void StrongFitAndLearningProgressFasterThanWeakFit()
        {
            PersonState strong = CreatePerson(41, traits: WorkerHiddenTraits.Create(90, 84, 88, 80, 94, 85));
            PersonState weak = CreatePerson(42, traits: WorkerHiddenTraits.Create(25, 22, 20, 50, 18, 30));
            WorkerSlotState slot = new("stock_helper", "Stock Helper", 1000, false);
            slot.Assign("worker", "Worker", 1000);

            int strongGain = ApprenticeshipProgressionEvaluator.AdvancePaidWeek(strong, BusinessType.GeneralStore, 4, slot);
            int weakGain = ApprenticeshipProgressionEvaluator.AdvancePaidWeek(weak, BusinessType.GeneralStore, 4, slot);

            Assert.Greater(strongGain, weakGain);
            Assert.Greater(strong.apprenticeship.ExperiencePoints, weak.apprenticeship.ExperiencePoints);
        }

        [Test]
        public void UnsupportedBusinessesDoNotAdvanceApprenticeship()
        {
            foreach (BusinessType businessType in new[] { BusinessType.CropFarm, BusinessType.Ranch, BusinessType.Doctor })
            {
                PersonState person = CreatePerson(50 + (int)businessType);
                WorkerSlotState slot = new("worker", "Worker", 1200, true);
                slot.Assign(person.id.ToString(), person.DisplayName, 1200);

                int gained = ApprenticeshipProgressionEvaluator.AdvancePaidWeek(person, businessType, 9, slot);

                Assert.AreEqual(0, gained, $"{businessType} should not advance apprenticeship in the MVP slice.");
                Assert.AreEqual(ApprenticeshipStage.Helper, person.apprenticeship.Stage);
                Assert.AreEqual(0, person.apprenticeship.ExperiencePoints);
            }
        }

        [Test]
        public void PopulationSaveLoadRoundTripsApprenticeshipAndDefaultsMissingState()
        {
            GameObject sourceObject = new("PopulationManager_ApprenticeshipSource");
            GameObject loadedObject = new("PopulationManager_ApprenticeshipLoaded");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                PopulationManager loaded = loadedObject.AddComponent<PopulationManager>();
                PersonState original = CreatePerson(60);
                original.apprenticeship = WorkerApprenticeshipState.FirstPassDefault();
                original.apprenticeship.RecordPaidWeek(75, (int)BusinessType.Blacksmith, 8, "striker_helper", ApprenticeshipStage.Apprentice);

                PopulationState state = new();
                state.people.Add(original);
                SetPrivateField(source, "state", state);

                LandLedgers.Persistence.PopulationSaveDto dto = source.CaptureSaveDto();
                dto.people.Add(CreateScheduledPersonDto(61, PopulationScheduleState.AtHome, 1, -1, 1));
                loaded.LoadFromSaveDto(dto);

                PersonState roundTripped = loaded.State.GetPerson(60);
                Assert.NotNull(roundTripped);
                Assert.AreEqual(ApprenticeshipStage.Apprentice, roundTripped.apprenticeship.Stage);
                Assert.AreEqual(75, roundTripped.apprenticeship.ExperiencePoints);
                Assert.AreEqual((int)BusinessType.Blacksmith, roundTripped.apprenticeship.CurrentBusinessType);
                Assert.AreEqual(8, roundTripped.apprenticeship.CurrentBuildingId);
                Assert.AreEqual("striker_helper", roundTripped.apprenticeship.CurrentSlotId);

                PersonState defaulted = loaded.State.GetPerson(61);
                Assert.NotNull(defaulted);
                Assert.IsTrue(defaulted.apprenticeship.IsInitialized);
                Assert.AreEqual(ApprenticeshipStage.Helper, defaulted.apprenticeship.Stage);
                Assert.AreEqual(0, defaulted.apprenticeship.ExperiencePoints);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(loadedObject);
            }
        }

        [Test]
        public void StaffingTextSuppressesGeneralStoreApprenticeshipButKeepsSkilledTradeProgress()
        {
            GameObject populationObject = new("PopulationManager_ApprenticeshipText");
            GameObject storeObject = new("GeneralStoreRuntimeManager_ApprenticeshipText");
            GameObject sharedObject = new("SharedBusinessRuntimeManager_ApprenticeshipText");
            GeneralStoreBusinessDefinition storeDefinition = ScriptableObject.CreateInstance<GeneralStoreBusinessDefinition>();
            try
            {
                PopulationManager populationManager = populationObject.AddComponent<PopulationManager>();
                PersonState storeWorker = CreatePerson(70, workplaceBuildingId: 3);
                storeWorker.apprenticeship = WorkerApprenticeshipState.FirstPassDefault();
                storeWorker.apprenticeship.experiencePoints = 21;
                PersonState smithWorker = CreatePerson(71, workplaceBuildingId: 5);
                smithWorker.apprenticeship = WorkerApprenticeshipState.FirstPassDefault();
                smithWorker.apprenticeship.experiencePoints = 120;
                smithWorker.apprenticeship.stage = ApprenticeshipStage.Apprentice;
                populationManager.State.people.Add(storeWorker);
                populationManager.State.people.Add(smithWorker);

                BusinessRuntimeState storeRuntime = new();
                SetPrivateField(storeRuntime, "workerSlots", new List<WorkerSlotState>
                {
                    AssignedSlot("clerk_helper", "Clerk / Helper", 1200, true, storeWorker)
                });
                GeneralStoreRuntimeManager storeManager = storeObject.AddComponent<GeneralStoreRuntimeManager>();
                SetPrivateField(storeManager, "populationManager", populationManager);
                SetPrivateField(storeManager, "runtimeState", storeRuntime);
                SetPrivateField(storeManager, "storeDefinition", storeDefinition);

                string storeText = storeManager.BuildStoreWorkersText();
                StringAssert.Contains("Staffing", storeText);
                StringAssert.Contains("Current Staff", storeText);
                StringAssert.Contains("Store Clerk", storeText);
                Assert.False(storeText.Contains("Clerk / Helper"), storeText);
                Assert.False(storeText.Contains("Helper 35% to Apprentice"), storeText);

                SharedBusinessRuntimeManager sharedManager = sharedObject.AddComponent<SharedBusinessRuntimeManager>();
                SetPrivateField(sharedManager, "populationManager", populationManager);
                BusinessInstanceState blacksmith = CreateBusinessInstanceWithWorker(
                    BusinessType.Blacksmith,
                    5,
                    AssignedSlot("striker_helper", "Striker / Helper", 1100, false, smithWorker));

                string sharedText = sharedManager.BuildManagementText(blacksmith, 0);
                StringAssert.Contains("Apprentice 50% to Worker", sharedText);
            }
            finally
            {
                Object.DestroyImmediate(storeDefinition);
                Object.DestroyImmediate(populationObject);
                Object.DestroyImmediate(storeObject);
                Object.DestroyImmediate(sharedObject);
            }
        }

        private static PersonState CreatePerson(
            int id,
            LaborAccessLevel laborAccess = LaborAccessLevel.FullLaborMarket,
            int workplaceBuildingId = -1,
            WorkerHiddenTraits traits = null)
        {
            WorkerHiddenTraits hiddenTraits = traits ?? WorkerHiddenTraits.Create(60, 60, 60, 60, 60, 60);
            return new PersonState
            {
                id = id,
                firstName = $"Test{id}",
                lastName = "Worker",
                age = laborAccess == LaborAccessLevel.JuniorLowTrust ? 14 : 28,
                ageBand = laborAccess == LaborAccessLevel.JuniorLowTrust ? AgeBand.JuniorWorker13To15 : AgeBand.Adult18Plus,
                laborAccessLevel = laborAccess,
                householdId = 1,
                professionId = string.Empty,
                professionName = "No assigned job",
                wage = WageSnapshot.None(),
                visibleWorkerProfile = WorkerVisibleProfile.Create(55, 1200, "No paid work history"),
                hiddenWorkerTraits = hiddenTraits,
                workerTraitVisibility = WorkerTraitVisibility.FirstPassDefault(),
                apprenticeship = WorkerApprenticeshipState.FirstPassDefault(),
                homeBuildingId = 0,
                workplaceBuildingId = workplaceBuildingId,
                scheduleState = PopulationScheduleState.AtHome,
                currentDestinationBuildingId = 0
            };
        }

        private static WorkerSlotState AssignedSlot(string slotId, string displayName, int wageCents, bool required, PersonState person)
        {
            WorkerSlotState slot = new(slotId, displayName, wageCents, required);
            slot.Assign(person.id.ToString(), person.DisplayName, wageCents);
            return slot;
        }

        private static BusinessInstanceState CreateBusinessInstanceWithWorker(BusinessType businessType, int buildingId, WorkerSlotState slot)
        {
            LandLedgers.Persistence.BusinessInstanceSaveDto dto = new()
            {
                instanceId = $"test_{businessType}_{buildingId}",
                profileId = businessType.ToString(),
                businessType = businessType,
                assignedBuildingId = buildingId,
                runtimeDisplayName = $"Test {businessType}",
                owner = new LandLedgers.Persistence.BusinessOwnerSaveDto
                {
                    ownerKind = BusinessOwnerKind.Player,
                    displayName = "Player",
                    surname = "Player"
                },
                runtime = new LandLedgers.Persistence.BusinessRuntimeSaveDto
                {
                    businessId = businessType.ToString(),
                    businessType = businessType,
                    currentCashCents = 10000
                }
            };
            dto.runtime.workerSlots.Add(slot.CaptureSaveDto());
            return BusinessInstanceState.FromSaveDto(dto);
        }

        private static LandLedgers.Persistence.PersonSaveDto CreateScheduledPersonDto(
            int id,
            PopulationScheduleState scheduleState,
            int homeBuildingId,
            int workplaceBuildingId,
            int currentDestinationBuildingId)
        {
            return new LandLedgers.Persistence.PersonSaveDto
            {
                id = id,
                firstName = $"Test{id}",
                lastName = "Worker",
                age = 28,
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = 1,
                professionId = string.Empty,
                professionName = "No assigned job",
                wage = WageSnapshot.None(),
                homeBuildingId = homeBuildingId,
                workplaceBuildingId = workplaceBuildingId,
                scheduleState = scheduleState,
                currentDestinationBuildingId = currentDestinationBuildingId
            };
        }

        private static void AssertTraitRange(int value)
        {
            Assert.GreaterOrEqual(value, 0);
            Assert.LessOrEqual(value, 100);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"{target.GetType().Name}.{fieldName} should exist.");
            field.SetValue(target, value);
        }
    }
}
