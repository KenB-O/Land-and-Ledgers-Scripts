using System;
using System.Text;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Civic
{
    public enum SchoolhousePressureLevel
    {
        None = 0,
        EarlySignal = 1,
        MountingSignal = 2,
        ActionSignal = 3,
        Established = 4
    }

    public enum SchoolhouseOperationalState
    {
        NotJustified = 0,
        PressureBuilding = 1,
        UnbuiltReady = 2,
        TeacherVacant = 3,
        Underused = 4,
        Operating = 5
    }

    [Flags]
    public enum SchoolhouseDiagnosticFlags
    {
        None = 0,
        MissingTownWorld = 1 << 0,
        MissingPopulationData = 1 << 1,
        MissingPublicSiteRole = 1 << 2,
        MissingPlacementBridge = 1 << 3,
        MissingPhysicalDefinition = 1 << 4,
        SavedReferenceMissing = 1 << 5,
        SavedReferencePlotMismatch = 1 << 6,
        SavedReferenceNotTagged = 1 << 7,
        TeacherVacant = 1 << 8,
        NoTeacherCandidate = 1 << 9,
        CapacityStrained = 1 << 10
    }

    public static class SchoolhouseDiagnosticsUtility
    {
        public static int CountIssues(SchoolhouseDiagnosticFlags flags)
        {
            int issueCount = 0;
            for (int bit = 1; bit <= (int)SchoolhouseDiagnosticFlags.CapacityStrained; bit <<= 1)
            {
                if ((((int)flags) & bit) != 0)
                {
                    issueCount++;
                }
            }

            return issueCount;
        }

        public static bool HasIntegrationIssues(SchoolhouseDiagnosticFlags flags)
        {
            return flags.HasFlag(SchoolhouseDiagnosticFlags.MissingTownWorld)
                || flags.HasFlag(SchoolhouseDiagnosticFlags.MissingPopulationData)
                || flags.HasFlag(SchoolhouseDiagnosticFlags.MissingPublicSiteRole)
                || flags.HasFlag(SchoolhouseDiagnosticFlags.MissingPlacementBridge)
                || flags.HasFlag(SchoolhouseDiagnosticFlags.MissingPhysicalDefinition);
        }

        public static bool HasSavedReferenceIssues(SchoolhouseDiagnosticFlags flags)
        {
            return flags.HasFlag(SchoolhouseDiagnosticFlags.SavedReferenceMissing)
                || flags.HasFlag(SchoolhouseDiagnosticFlags.SavedReferencePlotMismatch)
                || flags.HasFlag(SchoolhouseDiagnosticFlags.SavedReferenceNotTagged);
        }

        public static bool HasOperationalIssues(SchoolhouseDiagnosticFlags flags)
        {
            return flags.HasFlag(SchoolhouseDiagnosticFlags.TeacherVacant)
                || flags.HasFlag(SchoolhouseDiagnosticFlags.NoTeacherCandidate)
                || flags.HasFlag(SchoolhouseDiagnosticFlags.CapacityStrained);
        }
    }

    [Serializable]
    public struct SchoolhouseThresholds
    {
        [Min(0)]
        public int minimumHouseholds;

        [Min(0)]
        public int minimumPopulation;

        [Min(0)]
        public int minimumSchoolAgeChildren;

        [Min(0)]
        public int minimumStableHouseholds;

        [Min(1)]
        public int oneRoomCapacity;

        [Min(0)]
        public int teacherMinimumAge;

        [Min(0)]
        public int teacherWeeklyWageCents;

        public static SchoolhouseThresholds Default => new()
        {
            minimumHouseholds = 24,
            minimumPopulation = 110,
            minimumSchoolAgeChildren = 12,
            minimumStableHouseholds = 18,
            oneRoomCapacity = 28,
            teacherMinimumAge = 18,
            teacherWeeklyWageCents = 1800
        };

        public SchoolhouseThresholds Sanitized()
        {
            return new SchoolhouseThresholds
            {
                minimumHouseholds = Mathf.Max(0, minimumHouseholds),
                minimumPopulation = Mathf.Max(0, minimumPopulation),
                minimumSchoolAgeChildren = Mathf.Max(0, minimumSchoolAgeChildren),
                minimumStableHouseholds = Mathf.Max(0, minimumStableHouseholds),
                oneRoomCapacity = Mathf.Max(1, oneRoomCapacity),
                teacherMinimumAge = Mathf.Max(0, teacherMinimumAge),
                teacherWeeklyWageCents = Mathf.Max(0, teacherWeeklyWageCents)
            };
        }
    }

    [Serializable]
    public struct SchoolhouseContributionWeights
    {
        public const float MaxContribution01 = 0.12f;

        public float civicConfidence;
        public float townMaturity;
        public float familyAttractiveness;
        public float immigrationPull;
        public float landConfidence;
        public float laborStability;

        public static SchoolhouseContributionWeights Default => new()
        {
            civicConfidence = 0.03f,
            townMaturity = 0.04f,
            familyAttractiveness = 0.05f,
            immigrationPull = 0.025f,
            landConfidence = 0.02f,
            laborStability = 0.02f
        };

        public SchoolhouseContributionWeights Sanitized()
        {
            return new SchoolhouseContributionWeights
            {
                civicConfidence = Clamp(civicConfidence),
                townMaturity = Clamp(townMaturity),
                familyAttractiveness = Clamp(familyAttractiveness),
                immigrationPull = Clamp(immigrationPull),
                landConfidence = Clamp(landConfidence),
                laborStability = Clamp(laborStability)
            };
        }

        private static float Clamp(float value)
        {
            return Mathf.Clamp(value, 0f, MaxContribution01);
        }
    }

    public readonly struct SchoolhouseContribution
    {
        public SchoolhouseContribution(
            float civicConfidence01,
            float townMaturity01,
            float familyAttractiveness01,
            float immigrationPull01,
            float landConfidence01,
            float laborStability01)
        {
            CivicConfidence01 = Clamp(civicConfidence01);
            TownMaturity01 = Clamp(townMaturity01);
            FamilyAttractiveness01 = Clamp(familyAttractiveness01);
            ImmigrationPull01 = Clamp(immigrationPull01);
            LandConfidence01 = Clamp(landConfidence01);
            LaborStability01 = Clamp(laborStability01);
        }

        public float CivicConfidence01 { get; }
        public float TownMaturity01 { get; }
        public float FamilyAttractiveness01 { get; }
        public float ImmigrationPull01 { get; }
        public float LandConfidence01 { get; }
        public float LaborStability01 { get; }

        public static SchoolhouseContribution Zero => new(0f, 0f, 0f, 0f, 0f, 0f);

        public static SchoolhouseContribution FromWeights(SchoolhouseContributionWeights weights, float operationScale01)
        {
            SchoolhouseContributionWeights sanitized = weights.Sanitized();
            float scale = Mathf.Clamp01(operationScale01);
            return new SchoolhouseContribution(
                sanitized.civicConfidence * scale,
                sanitized.townMaturity * scale,
                sanitized.familyAttractiveness * scale,
                sanitized.immigrationPull * scale,
                sanitized.landConfidence * scale,
                sanitized.laborStability * scale);
        }

        private static float Clamp(float value)
        {
            return Mathf.Clamp(value, 0f, SchoolhouseContributionWeights.MaxContribution01);
        }
    }

    public readonly struct SchoolhouseEligibilitySnapshot
    {
        public SchoolhouseEligibilitySnapshot(
            int householdCount,
            int populationCount,
            int schoolAgeChildCount,
            int stableHouseholdCount,
            int requiredHouseholds,
            int requiredPopulation,
            int requiredSchoolAgeChildren,
            int requiredStableHouseholds,
            SchoolhousePressureLevel pressureLevel)
        {
            HouseholdCount = Mathf.Max(0, householdCount);
            PopulationCount = Mathf.Max(0, populationCount);
            SchoolAgeChildCount = Mathf.Max(0, schoolAgeChildCount);
            StableHouseholdCount = Mathf.Max(0, stableHouseholdCount);
            RequiredHouseholds = Mathf.Max(0, requiredHouseholds);
            RequiredPopulation = Mathf.Max(0, requiredPopulation);
            RequiredSchoolAgeChildren = Mathf.Max(0, requiredSchoolAgeChildren);
            RequiredStableHouseholds = Mathf.Max(0, requiredStableHouseholds);
            PressureLevel = pressureLevel;
        }

        public int HouseholdCount { get; }
        public int PopulationCount { get; }
        public int SchoolAgeChildCount { get; }
        public int StableHouseholdCount { get; }
        public int RequiredHouseholds { get; }
        public int RequiredPopulation { get; }
        public int RequiredSchoolAgeChildren { get; }
        public int RequiredStableHouseholds { get; }
        public SchoolhousePressureLevel PressureLevel { get; }
        public bool IsEligible => PressureLevel == SchoolhousePressureLevel.ActionSignal
            || PressureLevel == SchoolhousePressureLevel.Established;
        public float HouseholdReadiness01 => Ratio(HouseholdCount, RequiredHouseholds);
        public float PopulationReadiness01 => Ratio(PopulationCount, RequiredPopulation);
        public float ChildReadiness01 => Ratio(SchoolAgeChildCount, RequiredSchoolAgeChildren);
        public float StableHouseholdReadiness01 => Ratio(StableHouseholdCount, RequiredStableHouseholds);
        public float OverallReadiness01 => Mathf.Min(
            Mathf.Min(HouseholdReadiness01, PopulationReadiness01),
            Mathf.Min(ChildReadiness01, StableHouseholdReadiness01));

        public string BuildNeedSummary()
        {
            if (IsEligible)
            {
                return $"Schoolhouse justified: {HouseholdCount} households, {PopulationCount} people, {SchoolAgeChildCount} school-age children, {StableHouseholdCount} stable households.";
            }

            if (PressureLevel == SchoolhousePressureLevel.MountingSignal)
            {
                return $"Schoolhouse pressure mounting: {HouseholdCount}/{RequiredHouseholds} households, {PopulationCount}/{RequiredPopulation} people, {SchoolAgeChildCount} school-age children, {StableHouseholdCount}/{RequiredStableHouseholds} stable households.";
            }

            if (PressureLevel == SchoolhousePressureLevel.EarlySignal)
            {
                return $"Schooling need visible: {SchoolAgeChildCount} school-age children across {HouseholdCount} households, but the civic base is still short of a dedicated Schoolhouse.";
            }

            return "Schoolhouse not yet justified by household base, children, and town permanence.";
        }

        public string BuildReadinessSummary()
        {
            return $"Readiness {FormatPercent(OverallReadiness01)} | Households {HouseholdCount}/{RequiredHouseholds} | Population {PopulationCount}/{RequiredPopulation} | Children {SchoolAgeChildCount}/{RequiredSchoolAgeChildren} | Stable households {StableHouseholdCount}/{RequiredStableHouseholds}";
        }

        private static float Ratio(int value, int required)
        {
            return required <= 0 ? 1f : Mathf.Clamp01(value / (float)required);
        }

        private static string FormatPercent(float value01)
        {
            return $"{Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f)}%";
        }
    }

    public readonly struct SchoolhouseRuntimeSnapshot
    {
        public SchoolhouseRuntimeSnapshot(
            bool built,
            int buildingId,
            int plotId,
            string displayName,
            string ownerDisplayName,
            string civicStatusLabel,
            int teacherPersonId,
            string teacherDisplayName,
            SchoolhouseEligibilitySnapshot eligibility,
            int oneRoomCapacity,
            SchoolhouseOperationalState operationalState,
            SchoolhouseContribution contribution,
            float operationScale01 = 0f)
        {
            Built = built;
            BuildingId = buildingId;
            PlotId = plotId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Schoolhouse" : displayName.Trim();
            OwnerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName) ? "School Board" : ownerDisplayName.Trim();
            CivicStatusLabel = string.IsNullOrWhiteSpace(civicStatusLabel) ? "Civic" : civicStatusLabel.Trim();
            TeacherPersonId = teacherPersonId;
            TeacherDisplayName = teacherDisplayName ?? string.Empty;
            Eligibility = eligibility;
            OneRoomCapacity = Mathf.Max(1, oneRoomCapacity);
            OperationalState = operationalState;
            Contribution = contribution;
            OperationScale01 = Mathf.Clamp01(operationScale01);
        }

        public bool Built { get; }
        public int BuildingId { get; }
        public int PlotId { get; }
        public string DisplayName { get; }
        public string OwnerDisplayName { get; }
        public string CivicStatusLabel { get; }
        public int TeacherPersonId { get; }
        public string TeacherDisplayName { get; }
        public SchoolhouseEligibilitySnapshot Eligibility { get; }
        public int OneRoomCapacity { get; }
        public SchoolhouseOperationalState OperationalState { get; }
        public SchoolhouseContribution Contribution { get; }
        public float OperationScale01 { get; }
        public int SchoolAgeChildCount => Eligibility.SchoolAgeChildCount;
        public int AttendanceSupportedCount => OperationalState == SchoolhouseOperationalState.Operating
            || OperationalState == SchoolhouseOperationalState.Underused
                ? Mathf.Min(SchoolAgeChildCount, OneRoomCapacity)
                : 0;
        public int UnservedSchoolAgeChildCount => IsOperating
            ? Mathf.Max(0, SchoolAgeChildCount - AttendanceSupportedCount)
            : 0;
        public bool HasTeacher => TeacherPersonId >= 0 && !string.IsNullOrWhiteSpace(TeacherDisplayName);
        public bool IsOperating => OperationalState == SchoolhouseOperationalState.Operating
            || OperationalState == SchoolhouseOperationalState.Underused;
        public bool RequiresAction => OperationalState == SchoolhouseOperationalState.UnbuiltReady
            || OperationalState == SchoolhouseOperationalState.TeacherVacant
            || IsCapacityStrained;
        public bool IsCapacityStrained => UnservedSchoolAgeChildCount > 0;
        public float Readiness01 => Eligibility.OverallReadiness01;
        public float SupportedAttendanceShare01 => OneRoomCapacity <= 0
            ? 0f
            : Mathf.Clamp01(AttendanceSupportedCount / (float)OneRoomCapacity);
        public float SchoolAgeDemandCoverage01 => SchoolAgeChildCount <= 0
            ? 0f
            : Mathf.Clamp01(AttendanceSupportedCount / (float)SchoolAgeChildCount);
    }

    [Serializable]
    public sealed class SchoolhouseState
    {
        public const string SchoolhouseBuildingId = "schoolhouse";
        public const string TeacherProfessionId = "teacher";
        public const string TeacherProfessionName = "Teacher";

        public bool built;
        public int buildingId = -1;
        public int plotId = -1;
        public string holdingId = SchoolhouseBuildingId;
        public string displayName = "Schoolhouse";
        public CivicOwnerKind ownerKind = CivicOwnerKind.Town;
        public string ownerDisplayName = "School Board";
        public string civicStatusLabel = "Civic";
        public int teacherPersonId = -1;
        public string teacherDisplayName = string.Empty;

        public bool HasBuildingReference => buildingId >= 0 && plotId >= 0;
        public bool Built => built && HasBuildingReference;
        public bool HasTeacher => teacherPersonId >= 0 && !string.IsNullOrWhiteSpace(teacherDisplayName);

        public void MarkBuilt(int newBuildingId, int newPlotId)
        {
            built = newBuildingId >= 0 && newPlotId >= 0;
            buildingId = built ? newBuildingId : -1;
            plotId = built ? newPlotId : -1;
            NormalizeIdentity();
        }

        public void MarkBuilt(PlacedBuilding building)
        {
            if (building == null)
            {
                MarkUnbuilt();
                return;
            }

            MarkBuilt(building.id, building.plotId);
        }

        public void MarkUnbuilt()
        {
            built = false;
            buildingId = -1;
            plotId = -1;
            NormalizeIdentity();
        }

        public void AssignTeacher(PersonState teacher)
        {
            if (teacher == null)
            {
                ClearTeacher();
                return;
            }

            teacherPersonId = teacher.id;
            teacherDisplayName = teacher.DisplayName;
        }

        public void ClearTeacher()
        {
            teacherPersonId = -1;
            teacherDisplayName = string.Empty;
        }

        public bool MatchesBuildingReference(int candidateBuildingId, int candidatePlotId)
        {
            return HasBuildingReference
                && buildingId == candidateBuildingId
                && plotId == candidatePlotId;
        }

        public SchoolhouseRuntimeSnapshot CaptureRuntimeSnapshot(
            PopulationState population,
            SchoolhouseThresholds thresholds,
            SchoolhouseContributionWeights weights)
        {
            NormalizeIdentity();
            SchoolhouseThresholds sanitizedThresholds = thresholds.Sanitized();
            SchoolhouseEligibilitySnapshot eligibility = SchoolhouseEvaluator.CaptureEligibility(population, sanitizedThresholds);
            bool teacherValid = SchoolhouseEvaluator.IsTeacherAssignmentValid(this, population, sanitizedThresholds);
            SchoolhouseOperationalState operationalState = ResolveOperationalState(eligibility, teacherValid, sanitizedThresholds);
            float operationScale = CalculateOperationScale(operationalState, eligibility, sanitizedThresholds);
            return new SchoolhouseRuntimeSnapshot(
                Built,
                buildingId,
                plotId,
                displayName,
                ownerDisplayName,
                civicStatusLabel,
                teacherValid ? teacherPersonId : -1,
                teacherValid ? teacherDisplayName : string.Empty,
                eligibility,
                sanitizedThresholds.oneRoomCapacity,
                operationalState,
                SchoolhouseContribution.FromWeights(weights, operationScale),
                operationScale);
        }

        public SchoolhouseSaveDto CaptureSaveDto()
        {
            NormalizeIdentity();
            return new SchoolhouseSaveDto
            {
                built = Built,
                buildingId = Built ? buildingId : -1,
                plotId = Built ? plotId : -1,
                holdingId = holdingId,
                displayName = displayName,
                ownerKind = ownerKind,
                ownerDisplayName = ownerDisplayName,
                civicStatusLabel = civicStatusLabel,
                teacherPersonId = teacherPersonId,
                teacherDisplayName = teacherDisplayName
            };
        }

        public void LoadFromSaveDto(SchoolhouseSaveDto dto)
        {
            if (dto == null)
            {
                MarkUnbuilt();
                ClearTeacher();
                return;
            }

            built = dto.built;
            buildingId = dto.buildingId;
            plotId = dto.plotId;
            holdingId = dto.holdingId;
            displayName = dto.displayName;
            ownerKind = dto.ownerKind;
            ownerDisplayName = dto.ownerDisplayName;
            civicStatusLabel = dto.civicStatusLabel;
            teacherPersonId = dto.teacherPersonId;
            teacherDisplayName = dto.teacherDisplayName;

            if (!HasBuildingReference)
            {
                built = false;
                buildingId = -1;
                plotId = -1;
            }

            if (teacherPersonId < 0)
            {
                teacherDisplayName = string.Empty;
            }

            NormalizeIdentity();
        }

        public void NormalizeIdentity()
        {
            holdingId = NormalizeText(holdingId, SchoolhouseBuildingId);
            displayName = NormalizeText(displayName, "Schoolhouse");
            ownerDisplayName = NormalizeText(ownerDisplayName, "School Board");
            civicStatusLabel = NormalizeText(civicStatusLabel, "Civic");
            teacherDisplayName = teacherDisplayName ?? string.Empty;
        }

        private SchoolhouseOperationalState ResolveOperationalState(
            SchoolhouseEligibilitySnapshot eligibility,
            bool teacherValid,
            SchoolhouseThresholds thresholds)
        {
            if (!Built)
            {
                return eligibility.IsEligible
                    ? SchoolhouseOperationalState.UnbuiltReady
                    : eligibility.PressureLevel == SchoolhousePressureLevel.MountingSignal
                        || eligibility.PressureLevel == SchoolhousePressureLevel.EarlySignal
                            ? SchoolhouseOperationalState.PressureBuilding
                            : SchoolhouseOperationalState.NotJustified;
            }

            if (!teacherValid)
            {
                return SchoolhouseOperationalState.TeacherVacant;
            }

            return eligibility.SchoolAgeChildCount < Mathf.Max(4, thresholds.minimumSchoolAgeChildren / 2)
                ? SchoolhouseOperationalState.Underused
                : SchoolhouseOperationalState.Operating;
        }

        private static float CalculateOperationScale(
            SchoolhouseOperationalState operationalState,
            SchoolhouseEligibilitySnapshot eligibility,
            SchoolhouseThresholds thresholds)
        {
            if (operationalState != SchoolhouseOperationalState.Operating
                && operationalState != SchoolhouseOperationalState.Underused)
            {
                return 0f;
            }

            int capacity = Mathf.Max(1, thresholds.oneRoomCapacity);
            int children = Mathf.Max(0, eligibility.SchoolAgeChildCount);
            int attendanceSupported = Mathf.Min(children, capacity);
            float roomUse = Mathf.Clamp01(attendanceSupported / (float)capacity);
            float demandCoverage = children <= 0
                ? 0f
                : Mathf.Clamp01(attendanceSupported / (float)children);

            if (operationalState == SchoolhouseOperationalState.Underused)
            {
                // A staffed but thinly used one-room school still signals permanence, but
                // should not provide the same town/family pull as a well-used schoolhouse.
                float demandSignal = Mathf.Max(roomUse, eligibility.ChildReadiness01 * 0.75f);
                return Mathf.Clamp(Mathf.Lerp(0.22f, 0.68f, demandSignal), 0.18f, 0.72f);
            }

            float capacityScale = Mathf.Lerp(0.72f, 1f, demandCoverage);
            float readinessScale = Mathf.Lerp(0.86f, 1f, eligibility.OverallReadiness01);
            return Mathf.Clamp01(Mathf.Min(capacityScale, readinessScale));
        }

        private static string NormalizeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    public static class SchoolhouseEvaluator
    {
        public static SchoolhouseEligibilitySnapshot CaptureEligibility(
            PopulationState population,
            SchoolhouseThresholds thresholds)
        {
            SchoolhouseThresholds sanitized = thresholds.Sanitized();
            int households = CountHouseholds(population);
            int populationCount = CountPeople(population);
            int schoolAgeChildren = CountSchoolAgeChildren(population);
            int stableHouseholds = CountStableHouseholds(population);

            bool eligible = households >= sanitized.minimumHouseholds
                && populationCount >= sanitized.minimumPopulation
                && schoolAgeChildren >= sanitized.minimumSchoolAgeChildren
                && stableHouseholds >= sanitized.minimumStableHouseholds;

            SchoolhousePressureLevel pressure = eligible
                ? SchoolhousePressureLevel.ActionSignal
                : ResolvePressureLevel(households, populationCount, schoolAgeChildren, stableHouseholds, sanitized);

            return new SchoolhouseEligibilitySnapshot(
                households,
                populationCount,
                schoolAgeChildren,
                stableHouseholds,
                sanitized.minimumHouseholds,
                sanitized.minimumPopulation,
                sanitized.minimumSchoolAgeChildren,
                sanitized.minimumStableHouseholds,
                pressure);
        }

        public static bool TryAssignTeacher(
            SchoolhouseState schoolhouse,
            PopulationState population,
            SchoolhouseThresholds thresholds,
            out PersonState teacher,
            out string summary)
        {
            teacher = null;
            summary = string.Empty;
            if (schoolhouse == null || !schoolhouse.Built || population == null || population.people == null)
            {
                summary = "No built Schoolhouse or population records available for teacher assignment.";
                return false;
            }

            SchoolhouseThresholds sanitized = thresholds.Sanitized();
            if (IsTeacherAssignmentValid(schoolhouse, population, sanitized))
            {
                teacher = population.GetPerson(schoolhouse.teacherPersonId);
                summary = $"{teacher.DisplayName} remains assigned as Teacher.";
                return true;
            }

            if (!TryFindBestTeacherCandidate(population, sanitized, out PersonState candidate))
            {
                schoolhouse.ClearTeacher();
                summary = "No available adult teacher candidate is present.";
                return false;
            }

            candidate.professionId = SchoolhouseState.TeacherProfessionId;
            candidate.professionName = SchoolhouseState.TeacherProfessionName;
            candidate.wage = new WageSnapshot
            {
                weeklyWage = Mathf.RoundToInt(sanitized.teacherWeeklyWageCents / 100f),
                stabilityPercent = 82,
                currencyId = "dollars"
            };
            candidate.workplaceBuildingId = schoolhouse.buildingId;
            candidate.RecordVisibleWorkerRole(SchoolhouseState.TeacherProfessionName, sanitized.teacherWeeklyWageCents);
            schoolhouse.AssignTeacher(candidate);
            teacher = candidate;
            summary = $"{candidate.DisplayName} assigned as Teacher at the Schoolhouse.";
            return true;
        }

        public static bool IsTeacherAssignmentValid(SchoolhouseState schoolhouse, PopulationState population)
        {
            return IsTeacherAssignmentValid(schoolhouse, population, SchoolhouseThresholds.Default);
        }

        public static bool IsTeacherAssignmentValid(
            SchoolhouseState schoolhouse,
            PopulationState population,
            SchoolhouseThresholds thresholds)
        {
            if (schoolhouse == null || !schoolhouse.HasTeacher || population == null)
            {
                return false;
            }

            PersonState teacher = population.GetPerson(schoolhouse.teacherPersonId);
            return teacher != null
                && string.Equals(teacher.professionId, SchoolhouseState.TeacherProfessionId, StringComparison.OrdinalIgnoreCase)
                && teacher.workplaceBuildingId == schoolhouse.buildingId
                && CanServeAsTeacher(teacher, thresholds.Sanitized());
        }

        public static bool IsSchoolAge(PersonState person)
        {
            if (person == null)
            {
                return false;
            }

            return person.age >= 5 && person.age <= 15;
        }

        public static bool HasAvailableTeacherCandidate(PopulationState population, SchoolhouseThresholds thresholds)
        {
            return TryFindBestTeacherCandidate(population, thresholds.Sanitized(), out _);
        }

        private static bool CanServeAsTeacher(PersonState person, SchoolhouseThresholds thresholds)
        {
            if (person == null || person.age < thresholds.teacherMinimumAge)
            {
                return false;
            }

            if (person.laborAccessLevel < LaborAccessLevel.FullLaborMarket)
            {
                return false;
            }

            return person.workplaceBuildingId < 0
                || string.Equals(person.professionId, SchoolhouseState.TeacherProfessionId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryFindBestTeacherCandidate(
            PopulationState population,
            SchoolhouseThresholds thresholds,
            out PersonState teacher)
        {
            teacher = null;
            if (population == null || population.people == null)
            {
                return false;
            }

            int bestScore = int.MinValue;
            for (int i = 0; i < population.people.Count; i++)
            {
                PersonState candidate = population.people[i];
                if (!CanServeAsTeacher(candidate, thresholds))
                {
                    continue;
                }

                int score = CalculateTeacherCandidateScore(candidate, thresholds);
                if (teacher == null
                    || score > bestScore
                    || (score == bestScore && candidate.id < teacher.id))
                {
                    teacher = candidate;
                    bestScore = score;
                }
            }

            return teacher != null;
        }

        private static int CalculateTeacherCandidateScore(PersonState person, SchoolhouseThresholds thresholds)
        {
            int score = 0;

            if (string.Equals(person.professionId, SchoolhouseState.TeacherProfessionId, StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }

            if (person.workplaceBuildingId < 0)
            {
                score += 250;
            }

            // Keep the early schoolhouse grounded: prefer a mature, available adult without
            // inventing literacy/certification fields that may not exist in the population model yet.
            int idealTeacherAge = Mathf.Max(thresholds.teacherMinimumAge, 24);
            int ageDistance = Mathf.Abs(person.age - idealTeacherAge);
            score += Mathf.Max(0, 160 - ageDistance * 8);

            return score;
        }

        private static SchoolhousePressureLevel ResolvePressureLevel(
            int households,
            int population,
            int children,
            int stableHouseholds,
            SchoolhouseThresholds thresholds)
        {
            float householdReadiness = Ratio(households, thresholds.minimumHouseholds);
            float populationReadiness = Ratio(population, thresholds.minimumPopulation);
            float childReadiness = Ratio(children, thresholds.minimumSchoolAgeChildren);
            float stableReadiness = Ratio(stableHouseholds, thresholds.minimumStableHouseholds);
            float readiness = Mathf.Min(householdReadiness, populationReadiness, childReadiness, stableReadiness);

            if (readiness >= 0.78f && childReadiness >= 0.75f)
            {
                return SchoolhousePressureLevel.MountingSignal;
            }

            if (children >= Mathf.Max(6, Mathf.CeilToInt(thresholds.minimumSchoolAgeChildren * 0.55f))
                && households >= Mathf.CeilToInt(thresholds.minimumHouseholds * 0.65f))
            {
                return SchoolhousePressureLevel.EarlySignal;
            }

            return SchoolhousePressureLevel.None;
        }

        private static int CountHouseholds(PopulationState population)
        {
            return population != null && population.households != null ? population.households.Count : 0;
        }

        private static int CountPeople(PopulationState population)
        {
            return population != null && population.people != null ? population.people.Count : 0;
        }

        private static int CountSchoolAgeChildren(PopulationState population)
        {
            if (population == null || population.people == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < population.people.Count; i++)
            {
                if (IsSchoolAge(population.people[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountStableHouseholds(PopulationState population)
        {
            if (population == null || population.households == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < population.households.Count; i++)
            {
                HouseholdState household = population.households[i];
                if (household != null
                    && household.settlementArrangement == SettlementArrangement.StableHousehold
                    && !household.isTransientHousehold)
                {
                    count++;
                }
            }

            return count;
        }

        private static float Ratio(int value, int required)
        {
            return required <= 0 ? 1f : Mathf.Clamp01(value / (float)required);
        }
    }

    public static class SchoolhousePresentationUtility
    {
        public static string BuildStatusLine(SchoolhouseRuntimeSnapshot snapshot)
        {
            return snapshot.OperationalState switch
            {
                SchoolhouseOperationalState.Operating when snapshot.IsCapacityStrained => $"Operating | Teacher {snapshot.TeacherDisplayName} | Attendance {snapshot.AttendanceSupportedCount}/{snapshot.OneRoomCapacity} | {snapshot.UnservedSchoolAgeChildCount} children unserved",
                SchoolhouseOperationalState.Operating => $"Operating | Teacher {snapshot.TeacherDisplayName} | Attendance {snapshot.AttendanceSupportedCount}/{snapshot.OneRoomCapacity}",
                SchoolhouseOperationalState.Underused => $"Underused | Teacher {snapshot.TeacherDisplayName} | Attendance {snapshot.AttendanceSupportedCount}/{snapshot.OneRoomCapacity} | Benefit {FormatPercent(snapshot.OperationScale01)}",
                SchoolhouseOperationalState.TeacherVacant => "Built but closed: no teacher assigned.",
                SchoolhouseOperationalState.UnbuiltReady => "Town support is sufficient for a dedicated Schoolhouse.",
                SchoolhouseOperationalState.PressureBuilding => "Family schooling pressure is becoming visible.",
                _ => "Not yet justified by family base and settlement maturity."
            };
        }

        public static string BuildReadinessLine(SchoolhouseRuntimeSnapshot snapshot)
        {
            if (snapshot.Built)
            {
                if (!snapshot.IsOperating)
                {
                    return $"Built | Teacher needed | {snapshot.Eligibility.SchoolAgeChildCount} school-age children waiting";
                }

                string capacityText = snapshot.IsCapacityStrained
                    ? $" | {snapshot.UnservedSchoolAgeChildCount} over capacity"
                    : string.Empty;
                return $"Open | Attendance {snapshot.AttendanceSupportedCount}/{snapshot.OneRoomCapacity}{capacityText} | Benefit strength {FormatPercent(snapshot.OperationScale01)}";
            }

            return snapshot.Eligibility.BuildReadinessSummary();
        }

        public static string BuildActionLine(SchoolhouseRuntimeSnapshot snapshot)
        {
            return snapshot.OperationalState switch
            {
                SchoolhouseOperationalState.TeacherVacant => "Assign an available adult teacher before expecting schoolhouse benefits.",
                SchoolhouseOperationalState.Operating when snapshot.IsCapacityStrained => "Plan added schoolroom capacity before schooling demand outgrows the one-room site.",
                SchoolhouseOperationalState.UnbuiltReady => "Establish or tag a Schoolhouse site so the town can convert schooling pressure into civic benefit.",
                SchoolhouseOperationalState.PressureBuilding => "Watch schooling pressure; the family base is approaching the point where a dedicated Schoolhouse becomes justified.",
                SchoolhouseOperationalState.Underused => "Keep service visible, but treat the Schoolhouse as underused until more family demand arrives.",
                _ => string.Empty
            };
        }

        public static string BuildCompactSummary(SchoolhouseRuntimeSnapshot snapshot)
        {
            if (snapshot.IsOperating)
            {
                string capacityText = snapshot.IsCapacityStrained
                    ? $" | {snapshot.UnservedSchoolAgeChildCount} unserved"
                    : string.Empty;
                return $"Schoolhouse: Operating | Teacher {snapshot.TeacherDisplayName} | Children {snapshot.AttendanceSupportedCount}/{snapshot.OneRoomCapacity}{capacityText}";
            }

            if (snapshot.Built)
            {
                return $"Schoolhouse: Teacher missing | {snapshot.SchoolAgeChildCount} school-age children";
            }

            if (snapshot.Eligibility.IsEligible)
            {
                return $"Schoolhouse: Justified | {snapshot.SchoolAgeChildCount} school-age children";
            }

            return $"Schoolhouse: Not yet justified | {snapshot.SchoolAgeChildCount} school-age children";
        }

        public static string BuildContributionLine(SchoolhouseRuntimeSnapshot snapshot)
        {
            if (!snapshot.IsOperating)
            {
                return "Schoolhouse benefits inactive until the building is open and staffed.";
            }

            string stateText = snapshot.OperationalState == SchoolhouseOperationalState.Underused
                ? "Partial schoolhouse benefits"
                : snapshot.IsCapacityStrained
                    ? "Capacity-limited schoolhouse benefits"
                    : "Schoolhouse benefits active";
            return $"{stateText} at {FormatPercent(snapshot.OperationScale01)} strength | {BuildContributionLine(snapshot.Contribution)}";
        }

        public static string BuildContributionLine(SchoolhouseContribution contribution)
        {
            return $"Family attractiveness +{FormatPercent(contribution.FamilyAttractiveness01)} | Civic confidence +{FormatPercent(contribution.CivicConfidence01)} | Immigration pull +{FormatPercent(contribution.ImmigrationPull01)}";
        }

        public static string BuildDiagnosticSummaryLine(SchoolhouseDiagnosticFlags flags)
        {
            if (flags == SchoolhouseDiagnosticFlags.None)
            {
                return "Schoolhouse diagnostics clear.";
            }

            string state = SchoolhouseDiagnosticsUtility.HasIntegrationIssues(flags)
                ? "Integration needs review"
                : SchoolhouseDiagnosticsUtility.HasSavedReferenceIssues(flags)
                    ? "Reference needs review"
                    : "Operational attention needed";
            return $"{state} | {BuildIssueCountLabel(SchoolhouseDiagnosticsUtility.CountIssues(flags))}";
        }

        public static string BuildDiagnosticsText(SchoolhouseRuntimeSnapshot snapshot, SchoolhouseDiagnosticFlags flags)
        {
            if (flags == SchoolhouseDiagnosticFlags.None)
            {
                return string.Empty;
            }

            StringBuilder diagnostics = new StringBuilder();
            AppendIssue(diagnostics, flags, SchoolhouseDiagnosticFlags.MissingTownWorld, "Town world controller is not wired for Schoolhouse placement or reference checks.");
            AppendIssue(diagnostics, flags, SchoolhouseDiagnosticFlags.MissingPopulationData, "Population state is unavailable; schooling pressure, attendance, and teacher assignment cannot be trusted.");
            AppendIssue(diagnostics, flags, SchoolhouseDiagnosticFlags.MissingPublicSiteRole, "World model does not expose a Schoolhouse public-site role yet.");
            AppendIssue(diagnostics, flags, SchoolhouseDiagnosticFlags.MissingPlacementBridge, "Town world does not expose a civic-site placement bridge for Schoolhouse auto-establishment.");
            AppendIssue(diagnostics, flags, SchoolhouseDiagnosticFlags.MissingPhysicalDefinition, "No civic-compatible Schoolhouse physical definition is available.");
            AppendIssue(diagnostics, flags, SchoolhouseDiagnosticFlags.SavedReferenceMissing, "Saved Schoolhouse building reference no longer exists in the world list.");
            AppendIssue(diagnostics, flags, SchoolhouseDiagnosticFlags.SavedReferencePlotMismatch, "Saved Schoolhouse building and plot reference no longer match.");
            AppendIssue(diagnostics, flags, SchoolhouseDiagnosticFlags.SavedReferenceNotTagged, "Saved Schoolhouse building is no longer tagged as Schoolhouse.");

            if (flags.HasFlag(SchoolhouseDiagnosticFlags.TeacherVacant))
            {
                string teacherText = flags.HasFlag(SchoolhouseDiagnosticFlags.NoTeacherCandidate)
                    ? "Built Schoolhouse is closed because no valid teacher is assigned and no available adult candidate is present."
                    : "Built Schoolhouse is closed because no valid teacher is assigned.";
                AppendIssue(diagnostics, teacherText);
            }

            if (flags.HasFlag(SchoolhouseDiagnosticFlags.CapacityStrained))
            {
                AppendIssue(diagnostics, $"Schoolhouse capacity is strained: {snapshot.UnservedSchoolAgeChildCount} school-age children are not served by the one-room capacity.");
            }

            return diagnostics.ToString().TrimEnd();
        }

        private static void AppendIssue(StringBuilder diagnostics, SchoolhouseDiagnosticFlags flags, SchoolhouseDiagnosticFlags flag, string text)
        {
            if (flags.HasFlag(flag))
            {
                AppendIssue(diagnostics, text);
            }
        }

        private static void AppendIssue(StringBuilder diagnostics, string text)
        {
            diagnostics.Append("Schoolhouse issue: ").AppendLine(text);
        }

        private static string BuildIssueCountLabel(int count)
        {
            return count == 1 ? "1 issue" : $"{count} issues";
        }

        private static string FormatPercent(float value01)
        {
            return $"{Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f)}%";
        }
    }
}
