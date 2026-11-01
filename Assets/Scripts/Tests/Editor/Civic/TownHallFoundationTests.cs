using System;
using LandLedgers.Civic;
using LandLedgers.Economy;
using LandLedgers.Economy.Valuation;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Civic
{
    public sealed class TownHallFoundationTests
    {
        [Test]
        public void TownHallDefaultsUnbuiltWithZeroContribution()
        {
            TownHallState state = new();

            TownHallContribution contribution = state.CalculateContribution(TownHallContributionWeights.Default);

            Assert.IsFalse(state.Built);
            Assert.AreEqual(-1, state.buildingId);
            Assert.AreEqual(-1, state.plotId);
            Assert.AreEqual(0f, contribution.CivicConfidence01);
            Assert.AreEqual(0f, contribution.TownMaturity01);
            Assert.AreEqual(0f, contribution.ImmigrationPull01);
            Assert.AreEqual(0f, contribution.LandConfidence01);
        }

        [Test]
        public void BuiltTownHallProvidesDefaultContributions()
        {
            TownHallState state = new();
            state.MarkBuilt(12, 5);

            TownHallContribution contribution = state.CalculateContribution(TownHallContributionWeights.Default);

            Assert.IsTrue(state.Built);
            Assert.AreEqual(0.06f, contribution.CivicConfidence01, 0.0001f);
            Assert.AreEqual(0.08f, contribution.TownMaturity01, 0.0001f);
            Assert.AreEqual(0.03f, contribution.ImmigrationPull01, 0.0001f);
            Assert.AreEqual(0.04f, contribution.LandConfidence01, 0.0001f);
        }

        [Test]
        public void TownHallContributionTextReportsBuiltAndUnbuiltCleanly()
        {
            GameObject unbuiltObject = new("Town Hall Text Test");
            try
            {
                TownWorldController emptyWorld = unbuiltObject.AddComponent<TownWorldController>();
                CivicFoundationManager unbuiltCivic = unbuiltObject.AddComponent<CivicFoundationManager>();
                unbuiltCivic.Configure(emptyWorld);

                string unbuiltSummary = unbuiltCivic.BuildCompactSummaryText();
                string unbuiltDetail = unbuiltCivic.BuildDetailText();

                Assert.IsFalse(unbuiltCivic.TownHall.Built);
                StringAssert.Contains("Town Hall: Not built", unbuiltSummary);
                StringAssert.Contains("Civic confidence +0%", unbuiltSummary);
                StringAssert.Contains("Town Hall not built", unbuiltDetail);
                StringAssert.Contains("Maturity +0%", unbuiltDetail);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(unbuiltObject);
            }

            using TownHallTestWorld context = TownHallTestWorld.Create();
            CivicFoundationManager builtCivic = context.CreateCivicFoundation();

            string builtSummary = builtCivic.BuildCompactSummaryText();
            string builtDetail = builtCivic.BuildDetailText();

            Assert.IsTrue(builtCivic.TownHall.Built);
            StringAssert.Contains("Town Hall: Built", builtSummary);
            StringAssert.Contains("Owner: Town Council", builtDetail);
            StringAssert.Contains("Immigration pull hook", builtDetail);
            StringAssert.Contains("Land confidence", builtDetail);
        }

        [Test]
        public void TownHallContributionWeightsClamp()
        {
            TownHallState state = new();
            state.MarkBuilt(2, 1);
            TownHallContributionWeights weights = new()
            {
                civicConfidence = 0.7f,
                townMaturity = -0.1f,
                immigrationPull = 0.25f,
                landConfidence = 0.19f
            };

            TownHallContribution contribution = state.CalculateContribution(weights);

            Assert.AreEqual(0.2f, contribution.CivicConfidence01, 0.0001f);
            Assert.AreEqual(0f, contribution.TownMaturity01, 0.0001f);
            Assert.AreEqual(0.2f, contribution.ImmigrationPull01, 0.0001f);
            Assert.AreEqual(0.19f, contribution.LandConfidence01, 0.0001f);
        }

        [Test]
        public void GeneratedTownPlacesSingleTownHall()
        {
            using TownHallTestWorld context = TownHallTestWorld.Create();

            int count = 0;
            PlacedBuilding townHall = null;
            for (int i = 0; i < context.TownWorld.Buildings.Count; i++)
            {
                PlacedBuilding building = context.TownWorld.Buildings[i];
                if (building != null && building.publicSiteRole == PublicSiteRole.TownHall)
                {
                    count++;
                    townHall = building;
                }
            }

            Assert.AreEqual(1, count);
            Assert.NotNull(townHall);
            Assert.IsFalse(townHall.playerOwned);
            Assert.AreEqual("single_story_civic_natural_wood", townHall.definition.BuildingId);
            Assert.AreEqual(BuildingUseType.Civic, townHall.definition.PrimaryUse);
            Assert.IsFalse(townHall.definition.CanHostWorkplace);
            Assert.IsFalse(townHall.definition.CanHostHouseholds);
            Assert.AreEqual(PublicSiteRole.TownHall, context.TownWorld.Plots[townHall.plotId].publicSiteRole);
        }

        [Test]
        public void TownHallSaveRoundTrips()
        {
            CivicSaveDto dto = new()
            {
                townHall = new TownHallSaveDto
                {
                    built = true,
                    buildingId = 9,
                    plotId = 4,
                    holdingId = TownHallState.TownHallBuildingId,
                    displayName = "Town Hall",
                    ownerKind = CivicOwnerKind.Town,
                    ownerDisplayName = "Town Council",
                    civicStatusLabel = "Civic"
                }
            };

            string json = JsonUtility.ToJson(dto);
            CivicSaveDto restored = JsonUtility.FromJson<CivicSaveDto>(json);

            Assert.NotNull(restored);
            Assert.NotNull(restored.townHall);
            Assert.IsTrue(restored.townHall.built);
            Assert.AreEqual(9, restored.townHall.buildingId);
            Assert.AreEqual(4, restored.townHall.plotId);
            Assert.AreEqual(CivicOwnerKind.Town, restored.townHall.ownerKind);
        }

        [Test]
        public void OldSaveWithoutCivicLoads()
        {
            using TownHallTestWorld context = TownHallTestWorld.Create();
            CivicFoundationManager civic = context.CreateCivicFoundation();

            civic.LoadFromSaveDto(null);

            Assert.IsTrue(civic.TownHall.Built);
            Assert.GreaterOrEqual(civic.TownHall.buildingId, 0);
            Assert.GreaterOrEqual(civic.TownHall.plotId, 0);
        }

        [Test]
        public void LegacyTownHallBuildingIdLoadsToPhysicalShellAndCivicRole()
        {
            TownGenerationSettings sourceSettings = AssetDatabase.LoadAssetAtPath<TownGenerationSettings>("Assets/Core/World/DefaultTownGenerationSettings.asset");
            Assert.NotNull(sourceSettings);

            TownGenerationSettings settings = UnityEngine.Object.Instantiate(sourceSettings);
            GameObject root = new("Legacy Town Hall Save Test");
            try
            {
                TownWorldController townWorld = root.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                SaveReferenceResolver resolver = new(townWorld);
                WorldSaveDto save = CreateLegacyTownHallWorldDto();

                Assert.IsTrue(townWorld.LoadFromSaveDto(save, resolver, out string message), message);

                PlacedBuilding building = townWorld.Buildings[0];
                Assert.AreEqual("single_story_civic_natural_wood", building.definition.BuildingId);
                Assert.AreEqual(PublicSiteRole.TownHall, building.publicSiteRole);
                Assert.AreEqual(PublicSiteRole.TownHall, townWorld.Plots[0].publicSiteRole);

                CivicFoundationManager civic = root.AddComponent<CivicFoundationManager>();
                civic.Configure(townWorld);

                Assert.IsTrue(civic.TownHall.Built);
                Assert.AreEqual(TownHallState.TownHallBuildingId, civic.TownHall.holdingId);
                Assert.AreEqual("Town Hall", civic.TownHall.displayName);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void TownHallFeedsLandConfidenceHook()
        {
            using TownHallTestWorld context = TownHallTestWorld.Create();
            int plotId = context.FindEmptyNonAgriculturalPlotId();
            context.TownWorld.Plots[plotId].playerOwned = true;

            LandAppreciationState baseline = context.EvaluateLandAppreciation(plotId, null);
            CivicFoundationManager civic = context.CreateCivicFoundation();
            LandAppreciationState boosted = context.EvaluateLandAppreciation(plotId, civic);
            TownHallContribution contribution = civic.CurrentTownHallContribution;

            Assert.AreEqual(
                Mathf.Clamp01(baseline.townGrowthPressure01 + contribution.LandConfidence01),
                boosted.townGrowthPressure01,
                0.0001f);
            Assert.AreEqual(
                Mathf.Clamp01(baseline.developmentReadiness01 + contribution.TownMaturity01 * 0.5f),
                boosted.developmentReadiness01,
                0.0001f);
        }

        [Test]
        public void SchoolhouseEligibilityRequiresHouseholdsPopulationChildrenAndStableBase()
        {
            PopulationState lowChildTown = CreateSchoolhousePopulation(28, 118, 7, 24);
            SchoolhouseEligibilitySnapshot lowChildSnapshot = SchoolhouseEvaluator.CaptureEligibility(
                lowChildTown,
                SchoolhouseThresholds.Default);

            Assert.IsFalse(lowChildSnapshot.IsEligible);
            Assert.AreEqual(SchoolhousePressureLevel.EarlySignal, lowChildSnapshot.PressureLevel);
            StringAssert.Contains("7 school-age", lowChildSnapshot.BuildNeedSummary());

            PopulationState readyTown = CreateSchoolhousePopulation(28, 118, 14, 24);
            SchoolhouseEligibilitySnapshot readySnapshot = SchoolhouseEvaluator.CaptureEligibility(
                readyTown,
                SchoolhouseThresholds.Default);

            Assert.IsTrue(readySnapshot.IsEligible);
            Assert.AreEqual(SchoolhousePressureLevel.ActionSignal, readySnapshot.PressureLevel);
            StringAssert.Contains("Schoolhouse justified", readySnapshot.BuildNeedSummary());
        }

        [Test]
        public void SchoolhouseRequiresTeacherForOperationAndKeepsVacancyState()
        {
            SchoolhouseState schoolhouse = new();
            schoolhouse.MarkBuilt(6, 2);
            PopulationState population = CreateSchoolhousePopulation(28, 118, 14, 24);

            SchoolhouseRuntimeSnapshot vacant = schoolhouse.CaptureRuntimeSnapshot(
                population,
                SchoolhouseThresholds.Default,
                SchoolhouseContributionWeights.Default);

            Assert.IsTrue(vacant.Built);
            Assert.AreEqual(SchoolhouseOperationalState.TeacherVacant, vacant.OperationalState);
            Assert.AreEqual(0, vacant.AttendanceSupportedCount);
            Assert.AreEqual(0f, vacant.Contribution.FamilyAttractiveness01);

            Assert.IsTrue(SchoolhouseEvaluator.TryAssignTeacher(
                schoolhouse,
                population,
                SchoolhouseThresholds.Default,
                out PersonState teacher,
                out string assignmentSummary));

            SchoolhouseRuntimeSnapshot operating = schoolhouse.CaptureRuntimeSnapshot(
                population,
                SchoolhouseThresholds.Default,
                SchoolhouseContributionWeights.Default);

            Assert.NotNull(teacher);
            Assert.AreEqual("teacher", teacher.professionId);
            Assert.AreEqual("Teacher", teacher.professionName);
            StringAssert.Contains("Teacher", assignmentSummary);
            Assert.AreEqual(SchoolhouseOperationalState.Operating, operating.OperationalState);
            Assert.AreEqual(14, operating.SchoolAgeChildCount);
            Assert.AreEqual(14, operating.AttendanceSupportedCount);
            Assert.Greater(operating.Contribution.FamilyAttractiveness01, 0f);
        }

        [Test]
        public void SchoolhouseSaveRoundTrips()
        {
            CivicSaveDto dto = new()
            {
                schoolhouse = new SchoolhouseSaveDto
                {
                    built = true,
                    buildingId = 11,
                    plotId = 5,
                    holdingId = SchoolhouseState.SchoolhouseBuildingId,
                    displayName = "Schoolhouse",
                    ownerKind = CivicOwnerKind.Town,
                    ownerDisplayName = "School Board",
                    civicStatusLabel = "Civic",
                    teacherPersonId = 21,
                    teacherDisplayName = "Ada Reed"
                }
            };

            string json = JsonUtility.ToJson(dto);
            CivicSaveDto restored = JsonUtility.FromJson<CivicSaveDto>(json);

            Assert.NotNull(restored);
            Assert.NotNull(restored.schoolhouse);
            Assert.IsTrue(restored.schoolhouse.built);
            Assert.AreEqual(11, restored.schoolhouse.buildingId);
            Assert.AreEqual(5, restored.schoolhouse.plotId);
            Assert.AreEqual(21, restored.schoolhouse.teacherPersonId);
            Assert.AreEqual("Schoolhouse", restored.schoolhouse.displayName);
        }

        private sealed class TownHallTestWorld : IDisposable
        {
            private readonly GameObject root;
            private readonly TownGenerationSettings settings;

            private TownHallTestWorld(GameObject root, TownGenerationSettings settings)
            {
                this.root = root;
                this.settings = settings;
                TownWorld = root.AddComponent<TownWorldController>();
            }

            public TownWorldController TownWorld { get; }

            public static TownHallTestWorld Create()
            {
                TownGenerationSettings sourceSettings = AssetDatabase.LoadAssetAtPath<TownGenerationSettings>("Assets/Core/World/DefaultTownGenerationSettings.asset");
                Assert.NotNull(sourceSettings);
                Assert.NotNull(sourceSettings.townHallDefinition);
                Assert.NotNull(sourceSettings.townHallDefinition.PhysicalBuildingDefinition);

                TownGenerationSettings settings = UnityEngine.Object.Instantiate(sourceSettings);
                GameObject root = new("Town Hall Foundation Test World");
                TownHallTestWorld context = new(root, settings);
                context.TownWorld.Configure(settings, null, null);
                context.TownWorld.GenerateTownShell();
                return context;
            }

            public CivicFoundationManager CreateCivicFoundation()
            {
                CivicFoundationManager civic = root.GetComponent<CivicFoundationManager>();
                if (civic == null)
                {
                    civic = root.AddComponent<CivicFoundationManager>();
                }

                civic.Configure(TownWorld);
                return civic;
            }

            public int FindEmptyNonAgriculturalPlotId()
            {
                for (int i = 0; i < TownWorld.Plots.Count; i++)
                {
                    TownPlot plot = TownWorld.Plots[i];
                    if (plot != null && plot.zone != PlotZone.Agricultural && plot.buildingId < 0)
                    {
                        return plot.id;
                    }
                }

                Assert.Fail("No empty non-agricultural plot found in generated town.");
                return -1;
            }

            public LandAppreciationState EvaluateLandAppreciation(int plotId, CivicFoundationManager civic)
            {
                GameObject marketObject = new(civic == null ? "Town Hall Baseline Market" : "Town Hall Civic Market");
                try
                {
                    AcquisitionMarketManager market = marketObject.AddComponent<AcquisitionMarketManager>();
                    market.Configure(TownWorld, null, null, null, null, civic);
                    market.RebuildMarket();

                    Assert.IsTrue(market.TryGetLandAppreciationForPlot(plotId, out LandAppreciationState appreciation));
                    Assert.NotNull(appreciation);
                    return new LandAppreciationState
                    {
                        plotId = appreciation.plotId,
                        buildingId = appreciation.buildingId,
                        holdingKind = appreciation.holdingKind,
                        improvementState = appreciation.improvementState,
                        purchaseBasisCents = appreciation.purchaseBasisCents,
                        capitalizedImprovementCents = appreciation.capitalizedImprovementCents,
                        currentEstimatedValueCents = appreciation.currentEstimatedValueCents,
                        appreciationDeltaCents = appreciation.appreciationDeltaCents,
                        purchaseDayIndex = appreciation.purchaseDayIndex,
                        lastValuationDayIndex = appreciation.lastValuationDayIndex,
                        townGrowthPressure01 = appreciation.townGrowthPressure01,
                        developmentReadiness01 = appreciation.developmentReadiness01
                    };
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(marketObject);
                }
            }

            public void Dispose()
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (settings != null)
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
            }
        }

        private static WorldSaveDto CreateLegacyTownHallWorldDto()
        {
            return new WorldSaveDto
            {
                settingsName = "Legacy Town Hall Test Settings",
                seed = 1886,
                gridWidthCells = 80,
                gridDepthCells = 80,
                cellSizeMeters = 2f,
                gridOriginX = -80f,
                gridOriginY = 0f,
                gridOriginZ = -80f,
                plots =
                {
                    new PlotSaveDto
                    {
                        id = 0,
                        zone = PlotZone.Business,
                        bounds = CreateRectDto(34, 36, 10, 8),
                        candidateFootprint = CreateRectDto(34, 36, 10, 8),
                        siteSizeX = 10,
                        siteSizeY = 8,
                        intendedFootprintX = 6,
                        intendedFootprintY = 5,
                        frontageCells = 10,
                        depthCells = 8,
                        roadFrontageDirection = GridDirection.South,
                        roadAccessCell = CreateCoordDto(39, 35),
                        buildingId = 0
                    }
                },
                buildings =
                {
                    new BuildingSaveDto
                    {
                        id = 0,
                        plotId = 0,
                        buildingDefinitionId = TownHallState.TownHallBuildingId,
                        footprint = CreateRectDto(36, 37, 6, 5),
                        siteSizeX = 10,
                        siteSizeY = 8,
                        intendedFootprintX = 6,
                        intendedFootprintY = 5,
                        frontageDirection = GridDirection.South
                    }
                }
            };
        }

        private static PopulationState CreateSchoolhousePopulation(int households, int people, int schoolAgeChildren, int stableHouseholds)
        {
            PopulationState state = new();
            int personId = 0;
            for (int householdIndex = 0; householdIndex < households; householdIndex++)
            {
                HouseholdState household = new()
                {
                    id = householdIndex,
                    householdName = $"Schoolhouse Test Household {householdIndex}",
                    surname = $"Family{householdIndex}",
                    homeBuildingId = householdIndex,
                    settlementArrangement = householdIndex < stableHouseholds
                        ? SettlementArrangement.StableHousehold
                        : SettlementArrangement.Boarding
                };
                state.households.Add(household);
            }

            for (int i = 0; i < people; i++)
            {
                bool child = i < schoolAgeChildren;
                HouseholdState household = state.households[i % state.households.Count];
                PersonState person = new()
                {
                    id = personId++,
                    firstName = child ? "Clara" : "Thomas",
                    lastName = household.surname,
                    age = child ? 9 : 24,
                    ageBand = child ? AgeBand.Child0To9 : AgeBand.Adult18Plus,
                    laborAccessLevel = child ? LaborAccessLevel.None : LaborAccessLevel.FullLaborMarket,
                    householdId = household.id,
                    professionId = child ? "dependent_child" : string.Empty,
                    professionName = child ? "No assigned job" : "No assigned job",
                    wage = WageSnapshot.None(),
                    homeBuildingId = household.homeBuildingId,
                    workplaceBuildingId = -1
                };
                state.people.Add(person);
                household.memberIds.Add(person.id);
            }

            return state;
        }

        private static GridRectSaveDto CreateRectDto(int xMin, int zMin, int width, int depth)
        {
            return new GridRectSaveDto
            {
                xMin = xMin,
                zMin = zMin,
                width = width,
                depth = depth
            };
        }

        private static GridCoordSaveDto CreateCoordDto(int x, int z)
        {
            return new GridCoordSaveDto
            {
                x = x,
                z = z
            };
        }
    }
}
