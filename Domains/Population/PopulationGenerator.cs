using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Population
{
    public static class PopulationGenerator
    {
        private const string DependentChildProfessionId = "dependent_child";
        private const string HouseholdHelperProfessionId = "household_helper";
        private const string UnassignedProfessionName = "No assigned job";
        private const string HouseholdHelperProfessionName = "Household Helper";

        /// <summary>
        /// Pass V P1-3: generation order follows Canon 12.6G — population
        /// envelope and real persons first, demographics, kinship and marital
        /// relationships, family histories, households FROM domestic
        /// relationships, accommodation, economic roles/property/assets/
        /// employment, validation. The 55-95 Canon calibration is untouched
        /// (targets still come from PopulationGenerationSettings).
        ///
        /// When <paramref name="kinship"/> is supplied, generation records
        /// spouse and parent/child edges for every planned family unit.
        /// <paramref name="startYear"/>/<paramref name="startMonth"/>/<paramref name="startDay"/>
        /// is the scenario start date used for GeneratedExact birth dates.
        /// </summary>
        public static PopulationGenerationResult Generate(
            TownWorldController townWorld,
            PopulationGenerationSettings settings,
            IReadOnlyList<NewcomerSettlementBoardingOption> boardingHouseOptions = null,
            KinshipRegistry kinship = null,
            int startYear = 1892,
            int startMonth = 3,
            int startDay = 1)
        {
            PopulationState state = new();
            if (townWorld == null)
            {
                state.validationMessages.Add("ERROR: Population generation requires a TownWorldController.");
                return new PopulationGenerationResult(state, "Population generation failed: missing town world.", true);
            }

            if (townWorld.Grid == null || townWorld.Buildings == null || townWorld.Buildings.Count == 0)
            {
                state.validationMessages.Add("ERROR: Population generation requires a generated town shell with buildings.");
                return new PopulationGenerationResult(state, "Population generation failed: town shell has no buildings.", true);
            }

            settings = settings != null ? settings : ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            settings.Sanitize();

            int seed = (townWorld.Settings != null ? townWorld.Settings.seed : 0) + settings.seedOffset;
            System.Random random = new(seed);

            List<BuildingRuntimeInfo> buildings = BuildRuntimeInfo(townWorld);
            List<BuildingRuntimeInfo> homes = new();
            List<HomeAssignmentSlot> homeSlots = new();
            List<BuildingRuntimeInfo> workplaces = new();
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].isHome)
                {
                    homes.Add(buildings[i]);
                    for (int slotIndex = 0; slotIndex < buildings[i].residentHouseholdCapacity; slotIndex++)
                    {
                        homeSlots.Add(new HomeAssignmentSlot(buildings[i], slotIndex));
                    }
                }

                if (buildings[i].isWorkplace)
                {
                    workplaces.Add(buildings[i]);
                }
            }

            if (homeSlots.Count == 0)
            {
                state.validationMessages.Add("ERROR: No residential buildings were found for household home assignment.");
                return new PopulationGenerationResult(state, "Population generation failed: no homes found.", true);
            }

            if (workplaces.Count == 0)
            {
                state.validationMessages.Add("WARN: No business buildings were found; working-age people will remain unassigned.");
            }

            Shuffle(homeSlots, random);
            int targetHouseholds = ResolveStartingHouseholdTarget(settings, homeSlots.Count);
            int targetResidents = ResolveStartingResidentTarget(settings, targetHouseholds);

            // Pass V P1-3 (Canon 12.6G): persons first. Plan the demographic
            // cohort — real persons with plausible ages, kinship and marital
            // relationships with temporally coherent ages — BEFORE any
            // household exists. Households are formed FROM the family units.
            List<FamilyUnitPlan> cohort = DemographicCohortPlanner.PlanCohort(targetResidents, targetHouseholds, seed);
            List<BirthDate> cohortBirthDates = DemographicCohortPlanner.AssignBirthDates(
                DemographicCohortPlanner.Flatten(cohort),
                startYear, startMonth, startDay, seed,
                $"World generation (seed {seed}); scenario provenance, documented as generated.");

            int flatBirthIndex = 0;
            int homeSlotIndex = 0;
            for (int unitIndex = 0; unitIndex < cohort.Count && homeSlotIndex < homeSlots.Count; unitIndex++)
            {
                CreatePlannedHousehold(
                    state, settings, random, homeSlots[homeSlotIndex].building,
                    cohort[unitIndex], cohortBirthDates, ref flatBirthIndex,
                    kinship, startYear, startMonth, startDay);
                homeSlotIndex++;
            }

            List<NewcomerSettlementHomeOption> settlementHomes = NewcomerSettlementPlanner.BuildHomeOptions(townWorld, settings);
            NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, random, settlementHomes, boardingHouseOptions);
            AssignProfessionsAndWorkplaces(state, settings.GetProfessionDefinitions(), workplaces, random, settings);
            RecalculateHouseholdIncome(state);
            NewcomerSettlementPlanner.RefreshSettlementMetrics(state, settlementHomes, boardingHouseOptions);
            Validate(state, homes, workplaces, kinship, startYear, startMonth, startDay);

            bool hasErrors = false;
            for (int i = 0; i < state.validationMessages.Count; i++)
            {
                if (state.validationMessages[i].StartsWith("ERROR:", StringComparison.Ordinal))
                {
                    hasErrors = true;
                    break;
                }
            }

            string summary = BuildSummary(state, homeSlots.Count, workplaces.Count);
            return new PopulationGenerationResult(state, summary, hasErrors);
        }

        private static List<BuildingRuntimeInfo> BuildRuntimeInfo(TownWorldController townWorld)
        {
            Dictionary<int, TownPlot> plotsById = new();
            for (int i = 0; i < townWorld.Plots.Count; i++)
            {
                plotsById[townWorld.Plots[i].id] = townWorld.Plots[i];
            }

            List<BuildingRuntimeInfo> result = new(townWorld.Buildings.Count);
            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding building = townWorld.Buildings[i];
                PlotZone zone = PlotZone.MixedUse;
                if (plotsById.TryGetValue(building.plotId, out TownPlot plot))
                {
                    zone = plot.zone;
                }

                string buildingId = building.definition != null ? building.definition.BuildingId : string.Empty;
                string displayName = building.definition != null ? building.definition.DisplayName : $"Building {building.id}";
                bool idLooksResidential = ContainsIgnoreCase(buildingId, "house") || ContainsIgnoreCase(displayName, "house") || ContainsIgnoreCase(buildingId, "home");
                bool idLooksWorkplace =
                    ContainsIgnoreCase(buildingId, "store")
                    || ContainsIgnoreCase(buildingId, "business")
                    || ContainsIgnoreCase(buildingId, "saloon")
                    || ContainsIgnoreCase(displayName, "store")
                    || ContainsIgnoreCase(displayName, "business")
                    || ContainsIgnoreCase(displayName, "saloon");

                bool hasDefinitionUseData = building.definition != null;
                int residentHouseholdCapacity = hasDefinitionUseData
                    ? building.definition.ResidentHouseholdCapacity
                    : zone == PlotZone.Residential || idLooksResidential ? 1 : 0;
                bool isHome = residentHouseholdCapacity > 0;
                bool isWorkplace = hasDefinitionUseData
                    ? building.definition.CanHostWorkplace
                    : zone == PlotZone.Business || zone == PlotZone.MixedUse || idLooksWorkplace;
                bool isMixedUseHome = hasDefinitionUseData
                    ? building.definition.IsMixedUse || building.definition.UsesUpperFloorResidential
                    : zone == PlotZone.MixedUse && isHome;

                result.Add(new BuildingRuntimeInfo(building.id, buildingId, displayName, zone, isHome, isWorkplace, residentHouseholdCapacity, isMixedUseHome));
            }

            return result;
        }

        private static int ResolveStartingHouseholdTarget(PopulationGenerationSettings settings, int homeSlotCount)
        {
            int occupancyTarget = Mathf.RoundToInt(homeSlotCount * settings.householdOccupancyRate);
            int minimumTarget = Mathf.Max(1, settings.minimumStartingHouseholds);
            int maximumTarget = Mathf.Max(minimumTarget, settings.maximumStartingHouseholds);
            int configuredTarget = Mathf.Clamp(Mathf.Max(occupancyTarget, minimumTarget), minimumTarget, maximumTarget);
            return Mathf.Clamp(configuredTarget, 1, homeSlotCount);
        }

        private static int ResolveStartingResidentTarget(PopulationGenerationSettings settings, int targetHouseholds)
        {
            int minimumPossibleResidents = Mathf.Max(targetHouseholds, targetHouseholds * settings.minHouseholdSize);
            int maximumPossibleResidents = Mathf.Max(minimumPossibleResidents, targetHouseholds * settings.maxHouseholdSize);
            int preferredTarget = Mathf.Max(settings.minimumStartingResidents, settings.targetStartingResidents);
            preferredTarget = Mathf.Min(preferredTarget, settings.maximumStartingResidents);
            return Mathf.Clamp(preferredTarget, minimumPossibleResidents, maximumPossibleResidents);
        }

        private static void CreatePlannedHousehold(
            PopulationState state,
            PopulationGenerationSettings settings,
            System.Random random,
            BuildingRuntimeInfo home,
            FamilyUnitPlan unit,
            List<BirthDate> birthDates,
            ref int flatBirthIndex,
            KinshipRegistry kinship,
            int startYear,
            int startMonth,
            int startDay)
        {
            string surname = Pick(settings.surnames, random, "Hale");
            int householdId = state.AllocateNextHouseholdId();
            HouseholdState household = new()
            {
                id = householdId,
                surname = surname,
                householdName = $"{surname} Household",
                homeBuildingId = home.buildingId,
                demandSnapshot = HouseholdDemandSnapshot.Empty(),
                arrivalProfile = NewcomerArrivalProfile.SettledResident,
                settlementArrangement = SettlementArrangement.StableHousehold,
                baseBoardingCapacity = NewcomerSettlementPlanner.GetBaseBoardingCapacity(settings, home.isMixedUseHome, home.residentHouseholdCapacity > 0),
                settlementReserveStrengthCents = 0,
                lastSettlementSummary = "Stable resident household"
            };

            int[] personIds = new int[unit.members.Count];
            for (int memberIndex = 0; memberIndex < unit.members.Count; memberIndex++)
            {
                PlannedPerson planned = unit.members[memberIndex];
                BirthDate birthDate = flatBirthIndex < birthDates.Count ? birthDates[flatBirthIndex] : default;
                flatBirthIndex++;
                PersonState person = CreateSettlementPerson(
                    state.AllocateNextPersonId(),
                    householdId,
                    surname,
                    home.buildingId,
                    memberIndex,
                    settings,
                    random,
                    NewcomerArrivalProfile.SettledResident,
                    SettlementArrangement.StableHousehold,
                    planned.age,
                    0.45f,
                    6,
                    string.Empty,
                    0,
                    householdId,
                    0,
                    planned.isFemale,
                    birthDate);
                person.RefreshDerivedAge(startYear, startMonth, startDay);
                personIds[memberIndex] = person.id;
                household.memberIds.Add(person.id);
                state.people.Add(person);
            }

            if (kinship != null)
            {
                for (int memberIndex = 0; memberIndex < unit.members.Count; memberIndex++)
                {
                    PlannedPerson planned = unit.members[memberIndex];
                    if (planned.spousePlanIndex > memberIndex && planned.spousePlanIndex < personIds.Length)
                    {
                        kinship.AddSpouses(personIds[memberIndex], personIds[planned.spousePlanIndex], "generation");
                    }

                    if (planned.motherPlanIndex >= 0 && planned.motherPlanIndex < personIds.Length)
                    {
                        kinship.AddParentChild(personIds[planned.motherPlanIndex], personIds[memberIndex], "generation");
                    }

                    if (planned.fatherPlanIndex >= 0 && planned.fatherPlanIndex < personIds.Length)
                    {
                        kinship.AddParentChild(personIds[planned.fatherPlanIndex], personIds[memberIndex], "generation");
                    }
                }
            }

            household.EnsureHouseholdReservesInitialized(true);
            household.settlementReserveStrengthCents = Mathf.Max(household.settlementReserveStrengthCents, household.spendingMoneyCents);
            NewcomerSettlementEvaluator.RefreshHouseholdPressures(household);
            state.households.Add(household);
        }

        internal static PersonState CreateSettlementPerson(
            int personId,
            int householdId,
            string surname,
            int homeBuildingId,
            int memberIndex,
            PopulationGenerationSettings settings,
            System.Random random,
            NewcomerArrivalProfile arrivalProfile,
            SettlementArrangement settlementArrangement,
            int forcedAge,
            float laborUrgency01,
            int laborReadinessModifier,
            string preferredProfessionBias,
            int settlementDifficulty,
            int hostHouseholdId,
            int startingCashCents,
            bool? forceIsFemale = null,
            BirthDate birthDate = default)
        {
            bool useFemaleName = forceIsFemale ?? random.NextDouble() < 0.5d;
            int age = forcedAge >= 0 ? forcedAge : RollAge(memberIndex, random);
            AgeBand ageBand = GetAgeBand(age);
            LaborAccessLevel laborAccess = GetLaborAccessLevel(ageBand);
            string professionId = ageBand == AgeBand.Helper10To12 ? HouseholdHelperProfessionId : DependentChildProfessionId;
            string professionName = ageBand == AgeBand.Helper10To12 ? HouseholdHelperProfessionName : UnassignedProfessionName;

            PersonState person = new()
            {
                id = personId,
                firstName = useFemaleName
                    ? Pick(settings.femaleFirstNames, random, "Clara")
                    : Pick(settings.maleFirstNames, random, "Thomas"),
                lastName = surname,
                age = age,
                ageBand = ageBand,
                birthDate = birthDate,
                isFemale = useFemaleName,
                laborAccessLevel = laborAccess,
                householdId = householdId,
                professionId = professionId,
                professionName = professionName,
                wage = WageSnapshot.None(),
                homeBuildingId = homeBuildingId,
                workplaceBuildingId = -1,
                scheduleState = PopulationScheduleState.AtHome,
                currentDestinationBuildingId = homeBuildingId,
                arrivalProfile = arrivalProfile,
                settlementArrangement = settlementArrangement,
                laborUrgency01 = Mathf.Clamp01(laborUrgency01),
                laborReadinessModifier = laborReadinessModifier,
                preferredProfessionBias = preferredProfessionBias ?? string.Empty,
                settlementDifficulty = Mathf.Max(0, settlementDifficulty),
                hostHouseholdId = hostHouseholdId,
                startingCashCents = Mathf.Max(0, startingCashCents)
            };

            person.InitializeWorkerTraits(random);
            person.EnsureSettlementStateInitialized();
            return person;
        }

        internal static int RollAge(int memberIndex, System.Random random)
        {
            if (memberIndex == 0)
            {
                return random.Next(18, 66);
            }

            int roll = random.Next(0, 100);
            if (roll < 30)
            {
                return random.Next(0, 10);
            }

            if (roll < 45)
            {
                return random.Next(10, 13);
            }

            if (roll < 60)
            {
                return random.Next(13, 16);
            }

            if (roll < 70)
            {
                return random.Next(16, 18);
            }

            return random.Next(18, 76);
        }

        public static AgeBand GetAgeBand(int age)
        {
            if (age <= 9)
            {
                return AgeBand.Child0To9;
            }

            if (age <= 12)
            {
                return AgeBand.Helper10To12;
            }

            if (age <= 15)
            {
                return AgeBand.JuniorWorker13To15;
            }

            if (age <= 17)
            {
                return AgeBand.YoungWorker16To17;
            }

            return AgeBand.Adult18Plus;
        }

        public static LaborAccessLevel GetLaborAccessLevel(AgeBand ageBand)
        {
            return ageBand switch
            {
                AgeBand.Child0To9 => LaborAccessLevel.None,
                AgeBand.Helper10To12 => LaborAccessLevel.HouseholdHelper,
                AgeBand.JuniorWorker13To15 => LaborAccessLevel.JuniorLowTrust,
                AgeBand.YoungWorker16To17 => LaborAccessLevel.YoungWorker,
                AgeBand.Adult18Plus => LaborAccessLevel.FullLaborMarket,
                _ => LaborAccessLevel.None
            };
        }

        private static void AssignProfessionsAndWorkplaces(
            PopulationState state,
            IReadOnlyList<ProfessionDefinition> professions,
            List<BuildingRuntimeInfo> workplaces,
            System.Random random,
            PopulationGenerationSettings settings)
        {
            Dictionary<int, int> assignedWorkerCountsByBuilding = new();
            for (int i = 0; i < workplaces.Count; i++)
            {
                assignedWorkerCountsByBuilding[workplaces[i].buildingId] = 0;
            }

            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                if (!ShouldAssignWork(person, settings, random))
                {
                    continue;
                }

                if (TryPickProfessionAndWorkplace(
                        person,
                        professions,
                        workplaces,
                        assignedWorkerCountsByBuilding,
                        random,
                        out ProfessionDefinition profession,
                        out BuildingRuntimeInfo workplace))
                {
                    person.professionId = profession.ProfessionId;
                    person.professionName = profession.DisplayName;
                    person.wage = profession.RollWage(random);
                    person.RecordVisibleWorkerRole(profession.DisplayName, person.wage.weeklyWage * 100);
                    person.workplaceBuildingId = workplace.buildingId;
                    assignedWorkerCountsByBuilding[workplace.buildingId] = assignedWorkerCountsByBuilding[workplace.buildingId] + 1;
                }
            }
        }

        private static bool ShouldAssignWork(PersonState person, PopulationGenerationSettings settings, System.Random random)
        {
            if (person == null || !NewcomerSettlementEvaluator.IsAvailableForLabor(person, LaborAccessLevel.JuniorLowTrust))
            {
                return false;
            }

            float chance = person.ageBand switch
            {
                AgeBand.JuniorWorker13To15 => settings.juniorWorkerChance,
                AgeBand.YoungWorker16To17 => settings.youngWorkerChance,
                AgeBand.Adult18Plus => settings.adultWorkerChance,
                _ => 0f
            };
            chance += (person.laborUrgency01 - 0.5f) * 0.18f;
            chance += (NewcomerSettlementEvaluator.GetLaborReadiness01(person) - 0.5f) * 0.16f;
            return random.NextDouble() <= Mathf.Clamp01(chance);
        }

        private static bool TryPickProfessionAndWorkplace(
            PersonState person,
            IReadOnlyList<ProfessionDefinition> professions,
            List<BuildingRuntimeInfo> workplaces,
            Dictionary<int, int> assignedWorkerCountsByBuilding,
            System.Random random,
            out ProfessionDefinition pickedProfession,
            out BuildingRuntimeInfo pickedWorkplace)
        {
            List<ProfessionWorkplaceCandidate> candidates = new();
            for (int professionIndex = 0; professionIndex < professions.Count; professionIndex++)
            {
                ProfessionDefinition profession = professions[professionIndex];
                if (profession == null || !profession.CanAssignTo(person))
                {
                    continue;
                }

                for (int workplaceIndex = 0; workplaceIndex < workplaces.Count; workplaceIndex++)
                {
                    BuildingRuntimeInfo workplace = workplaces[workplaceIndex];
                    if (!profession.IsCompatibleWith(workplace.definitionId, workplace.plotZone))
                    {
                        continue;
                    }

                    int currentCount = assignedWorkerCountsByBuilding.TryGetValue(workplace.buildingId, out int count) ? count : 0;
                    if (currentCount >= profession.MaxWorkersPerWorkplace)
                    {
                        continue;
                    }

                    candidates.Add(new ProfessionWorkplaceCandidate(profession, workplace));
                }
            }

            if (candidates.Count == 0)
            {
                pickedProfession = null;
                pickedWorkplace = default;
                return false;
            }

            ProfessionWorkplaceCandidate picked = PickWeightedProfessionCandidate(person, candidates, random);
            pickedProfession = picked.profession;
            pickedWorkplace = picked.workplace;
            return true;
        }

        private static ProfessionWorkplaceCandidate PickWeightedProfessionCandidate(
            PersonState person,
            List<ProfessionWorkplaceCandidate> candidates,
            System.Random random)
        {
            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += NewcomerSettlementEvaluator.GetProfessionWeight(person, candidates[i].profession);
            }

            if (totalWeight <= 0)
            {
                return candidates[random.Next(0, candidates.Count)];
            }

            int roll = random.Next(0, totalWeight);
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= NewcomerSettlementEvaluator.GetProfessionWeight(person, candidates[i].profession);
                if (roll < 0)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }

        private static void RecalculateHouseholdIncome(PopulationState state)
        {
            for (int householdIndex = 0; householdIndex < state.households.Count; householdIndex++)
            {
                HouseholdState household = state.households[householdIndex];
                int weeklyIncome = 0;
                for (int memberIndex = 0; memberIndex < household.memberIds.Count; memberIndex++)
                {
                    PersonState person = state.GetPerson(household.memberIds[memberIndex]);
                    if (person != null)
                    {
                        weeklyIncome += person.wage.weeklyWage;
                    }
                }

                household.weeklyIncomeSnapshot = weeklyIncome;
                household.spendingMoneyCents = Mathf.Max(household.spendingMoneyCents, Mathf.Max(250, weeklyIncome * 100));
            }
        }

        private static void Validate(
            PopulationState state,
            List<BuildingRuntimeInfo> homes,
            List<BuildingRuntimeInfo> workplaces,
            KinshipRegistry kinship,
            int startYear,
            int startMonth,
            int startDay)
        {
            HashSet<int> homeIds = BuildBuildingIdSet(homes);
            HashSet<int> workplaceIds = BuildBuildingIdSet(workplaces);

            HashSet<int> personIds = new();
            int spouseEdgeCount = 0;
            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                if (!personIds.Add(person.id))
                {
                    state.validationMessages.Add($"ERROR: Duplicate person id {person.id}.");
                }

                // Pass V P0-2/P1-3: every generated person carries a valid birth
                // date whose derived age matches the planned age.
                if (!person.birthDate.IsValid)
                {
                    state.validationMessages.Add($"ERROR: Person {person.id} has no valid birth date.");
                }
                else
                {
                    int derivedAge = AgeAuthority.CompletedYears(person.birthDate, startYear, startMonth, startDay);
                    if (derivedAge != person.age)
                    {
                        state.validationMessages.Add(
                            $"ERROR: Person {person.id} age {person.age} disagrees with birth date {person.birthDate.Year}-{person.birthDate.Month}-{person.birthDate.Day} (derives {derivedAge}).");
                    }

                    if (person.birthDate.Provenance != BirthDateProvenance.GeneratedExact)
                    {
                        state.validationMessages.Add(
                            $"WARN: Person {person.id} birth date provenance is {person.birthDate.Provenance}, expected GeneratedExact at generation.");
                    }
                }

                if (kinship != null)
                {
                    spouseEdgeCount += kinship.GetSpouses(person.id).Count;
                }

                HouseholdState personHousehold = state.GetHousehold(person.householdId);
                personHousehold?.EnsureSettlementStateInitialized();
                bool workplaceBackedLodging = UsesWorkplaceBackedLodging(personHousehold, person.homeBuildingId, workplaceIds);
                if (!homeIds.Contains(person.homeBuildingId) && !workplaceBackedLodging)
                {
                    state.validationMessages.Add($"ERROR: Person {person.id} has invalid home building {person.homeBuildingId}.");
                }

                if (person.workplaceBuildingId >= 0 && !workplaceIds.Contains(person.workplaceBuildingId))
                {
                    state.validationMessages.Add($"ERROR: Person {person.id} has invalid workplace building {person.workplaceBuildingId}.");
                }
            }

            for (int i = 0; i < state.households.Count; i++)
            {
                HouseholdState household = state.households[i];
                household?.EnsureSettlementStateInitialized();
                bool workplaceBackedLodging = UsesWorkplaceBackedLodging(household, household != null ? household.homeBuildingId : -1, workplaceIds);
                if (household != null && !homeIds.Contains(household.homeBuildingId) && !workplaceBackedLodging)
                {
                    state.validationMessages.Add($"ERROR: Household {household.id} has invalid home building {household.homeBuildingId}.");
                }

                // Guest-room households are the only empty households that should survive validation.
                // Other empty lodging households now indicate stale settlement state after the guest/lodging split was preserved.
                if (household != null
                    && household.memberIds.Count == 0
                    && !household.isBoardingHouseGuestHousehold)
                {
                    state.validationMessages.Add($"ERROR: Household {household.id} has no members.");
                }
            }

            // Pass V P1-3: kinship coherence — generation must create real
            // family edges, and every parent/child edge must be temporally
            // coherent (parents 17-44 years older than their children).
            if (kinship != null)
            {
                if (spouseEdgeCount == 0 && state.people.Count > 0)
                {
                    state.validationMessages.Add("WARN: generation created zero spouse edges.");
                }

                for (int i = 0; i < state.people.Count; i++)
                {
                    PersonState child = state.people[i];
                    if (child == null)
                    {
                        continue;
                    }

                    foreach (int parentId in kinship.GetParents(child.id))
                    {
                        PersonState parent = state.GetPerson(parentId);
                        if (parent == null)
                        {
                            state.validationMessages.Add($"ERROR: Person {child.id} has kinship parent {parentId} with no person record.");
                            continue;
                        }

                        int gap = parent.age - child.age;
                        if (gap < DemographicCohortPlanner.MinParentChildGapYears
                            || gap > DemographicCohortPlanner.MaxParentChildGapYears)
                        {
                            state.validationMessages.Add(
                                $"ERROR: Person {child.id} (age {child.age}) has parent P{parentId} (age {parent.age}): incoherent age gap.");
                        }
                    }
                }
            }
        }

        private static HashSet<int> BuildBuildingIdSet(List<BuildingRuntimeInfo> buildings)
        {
            HashSet<int> result = new();
            if (buildings == null)
            {
                return result;
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                result.Add(buildings[i].buildingId);
            }

            return result;
        }

        private static bool UsesWorkplaceBackedLodging(HouseholdState household, int buildingId, HashSet<int> workplaceIds)
        {
            if (household == null || buildingId < 0 || workplaceIds == null || !workplaceIds.Contains(buildingId))
            {
                return false;
            }

            if (household.isBoardingHouseGuestHousehold)
            {
                return true;
            }

            return household.isBoardingHouseLodging
                && household.dwellingKind == HouseholdDwellingKind.BoardingHouseLodging;
        }

        private static string BuildSummary(PopulationState state, int homeCount, int workplaceCount)
        {
            int workers = 0;
            int weeklyIncome = 0;
            for (int i = 0; i < state.people.Count; i++)
            {
                if (state.people[i].workplaceBuildingId >= 0)
                {
                    workers++;
                }

                weeklyIncome += state.people[i].wage.weeklyWage;
            }

            StringBuilder builder = new();
            builder.Append("Population generated: ");
            builder.Append(state.households.Count);
            builder.Append(" households, ");
            builder.Append(state.people.Count);
            builder.Append(" people, ");
            builder.Append(workers);
            builder.Append(" assigned workers, $");
            builder.Append(weeklyIncome);
            builder.Append(" weekly wages. HomeSlots=");
            builder.Append(homeCount);
            builder.Append(", Workplaces=");
            builder.Append(workplaceCount);
            builder.Append(". ");
            builder.Append(state.lastSettlementSummary);
            return builder.ToString();
        }

        internal static string Pick(string[] values, System.Random random, string fallback)
        {
            if (values == null || values.Length == 0)
            {
                return fallback;
            }

            return values[random.Next(0, values.Length)];
        }

        private static void Shuffle<T>(List<T> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static bool ContainsIgnoreCase(string source, string value)
        {
            return !string.IsNullOrEmpty(source)
                && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private readonly struct BuildingRuntimeInfo
        {
            public BuildingRuntimeInfo(
                int buildingId,
                string definitionId,
                string displayName,
                PlotZone plotZone,
                bool isHome,
                bool isWorkplace,
                int residentHouseholdCapacity,
                bool isMixedUseHome)
            {
                this.buildingId = buildingId;
                this.definitionId = definitionId;
                this.displayName = displayName;
                this.plotZone = plotZone;
                this.isHome = isHome;
                this.isWorkplace = isWorkplace;
                this.residentHouseholdCapacity = Mathf.Max(0, residentHouseholdCapacity);
                this.isMixedUseHome = isMixedUseHome;
            }

            public readonly int buildingId;
            public readonly string definitionId;
            public readonly string displayName;
            public readonly PlotZone plotZone;
            public readonly bool isHome;
            public readonly bool isWorkplace;
            public readonly int residentHouseholdCapacity;
            public readonly bool isMixedUseHome;
        }

        private readonly struct HomeAssignmentSlot
        {
            public HomeAssignmentSlot(BuildingRuntimeInfo building, int slotIndex)
            {
                this.building = building;
                this.slotIndex = slotIndex;
            }

            public readonly BuildingRuntimeInfo building;
            public readonly int slotIndex;
        }

        private readonly struct ProfessionWorkplaceCandidate
        {
            public ProfessionWorkplaceCandidate(ProfessionDefinition profession, BuildingRuntimeInfo workplace)
            {
                this.profession = profession;
                this.workplace = workplace;
            }

            public readonly ProfessionDefinition profession;
            public readonly BuildingRuntimeInfo workplace;
        }
    }
}
