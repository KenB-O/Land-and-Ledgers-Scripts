using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Civic;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
using LandLedgers.UI;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class OpportunityPressureNoticeTests
    {
        [Test]
        public void RetailNoticeAppearsWhenTownPulseDemandIsMissed()
        {
            List<Object> cleanup = new();
            try
            {
                TownPulseRuntimeManager pulse = CreateComponent<TownPulseRuntimeManager>("Retail Pulse Source", cleanup);
                pulse.BeginDailyResolution(4);
                pulse.RecordDailyFulfillment("tools_hardware", 3, 0);
                pulse.CompleteDailyResolution(4);

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, null, pulse, null, null);

                Assert.IsTrue(manager.TryGetNotice("retail_lost_demand", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Retail, notice.PressureType);
                StringAssert.Contains("Missed 3", notice.Detail);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void TownPulseMissedUnitsAreScopedToResolvedDay()
        {
            List<Object> cleanup = new();
            try
            {
                TownPulseRuntimeManager pulse = CreateComponent<TownPulseRuntimeManager>("Scoped Pulse Source", cleanup);
                pulse.BeginDailyResolution(4);
                pulse.RecordDailyFulfillment("tools_hardware", 3, 0);
                pulse.CompleteDailyResolution(4);

                Assert.AreEqual(3, pulse.GetMissedUnitsForDay(4));
                Assert.AreEqual(3, pulse.GetMissedUnitsForCategoryForDay(4, "hardware"));
                Assert.AreEqual(0, pulse.GetMissedUnitsForDay(5));
                Assert.AreEqual(0, pulse.GetMissedUnitsForCategoryForDay(5, "hardware"));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void TownPulseAlertDoesNotReusePreviousDayFulfillmentBeforeCurrentDayResolution()
        {
            List<Object> cleanup = new();
            try
            {
                TownPulseRuntimeManager pulse = CreateComponent<TownPulseRuntimeManager>("Fresh Pulse Alert Source", cleanup);
                pulse.BeginDailyResolution(4);
                pulse.RecordDailyFulfillment("tools_hardware", 3, 3);
                pulse.CompleteDailyResolution(4);

                string nextDayAlert = InvokeTownPulseAlertText(pulse, 5);

                StringAssert.Contains("today 0/3", nextDayAlert);
                Assert.IsFalse(nextDayAlert.Contains("today 3/3"), nextDayAlert);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void HardwareSupplyNoticeAppearsWhenBlacksmithStockIsEmpty()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager shared = CreateComponent<SharedBusinessRuntimeManager>("Hardware Notice Shared Runtime", cleanup);
                BusinessInstanceState blacksmith = BusinessInstanceState.Create(
                    "hardware_notice_blacksmith",
                    LoadProfile(BusinessType.Blacksmith),
                    3,
                    BusinessOwnerIdentity.Npc(30, "Jonas Bell", "Bell"));
                CategoryStockState stock = blacksmith.RuntimeState.GetCategoryStock("tools_hardware");
                Assert.NotNull(stock);
                stock.SetCurrentStockForTests(0);
                SetSharedBusinesses(shared, blacksmith);

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, shared, null, null, null);

                Assert.IsTrue(manager.TryGetNotice("hardware_supply_shortage", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Retail, notice.PressureType);
                StringAssert.Contains("regional freight", notice.Detail.ToLowerInvariant());
                StringAssert.Contains("blacksmith", notice.ActionText.ToLowerInvariant());
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void HardwareSupplyNoticeAppearsWhenBlacksmithIsMissing()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager shared = CreateComponent<SharedBusinessRuntimeManager>("Missing Hardware Notice Shared Runtime", cleanup);
                SetSharedBusinesses(shared);

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, shared, null, null, null);

                Assert.IsTrue(manager.TryGetNotice("hardware_supply_shortage", out OpportunityNotice notice));
                StringAssert.Contains("No blacksmith", notice.Detail);
                StringAssert.Contains("regional freight", notice.Detail.ToLowerInvariant());
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void HousingNoticeAppearsAtNinetyPercentOccupancy()
        {
            List<Object> cleanup = new();
            try
            {
                TownWorldController townWorld = CreateComponent<TownWorldController>("Housing Notice Town", cleanup);
                PopulationManager population = CreateComponent<PopulationManager>("Housing Notice Population", cleanup);
                AddResidentialBuildings(townWorld, 10, cleanup);
                AddHouseholds(population, 9);

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(townWorld, population, null, null, null, null, null);

                Assert.IsTrue(manager.TryGetNotice("housing_near_capacity", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Housing, notice.PressureType);
                StringAssert.Contains("9/10", notice.Detail);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void HousingNoticeSeparatesImmigrationRiskFromBuildOpportunity()
        {
            List<Object> cleanup = new();
            try
            {
                TownWorldController townWorld = CreateComponent<TownWorldController>("Immigration Housing Notice Town", cleanup);
                PopulationManager population = CreateComponent<PopulationManager>("Immigration Housing Notice Population", cleanup);
                AddResidentialBuildings(townWorld, 8, cleanup);
                AddHouseholds(population, 8);
                population.State.rentalApplicantCount = 5;
                population.State.strongerHousingNeedCount = 4;
                population.State.rentalVacancyCount = 0;
                population.State.rentalPressure01 = 1f;

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(townWorld, population, null, null, null, null, null);

                Assert.IsTrue(manager.TryGetNotice("immigration_housing_pressure", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Housing, notice.PressureType);
                Assert.AreEqual(OpportunityNoticeSeverity.Urgent, notice.Severity);
                StringAssert.Contains("5 waiting applicants", notice.Detail);
                StringAssert.Contains("Immediate risk", notice.ActionText);
                StringAssert.Contains("Medium-term opening", manager.BuildNoticeBoardText());
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void LaborNoticeAppearsWhenRequiredSlotsHaveNoWorkers()
        {
            List<Object> cleanup = new();
            try
            {
                PopulationManager population = CreateComponent<PopulationManager>("Labor Notice Population", cleanup);
                SharedBusinessRuntimeManager shared = CreateComponent<SharedBusinessRuntimeManager>("Labor Notice Shared Runtime", cleanup);
                BusinessInstanceState blacksmith = BusinessInstanceState.Create(
                    "labor_notice_blacksmith",
                    LoadProfile(BusinessType.Blacksmith),
                    3,
                    BusinessOwnerIdentity.Player());
                SetSharedBusinesses(shared, blacksmith);

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, population, null, shared, null, null, null);

                Assert.IsTrue(manager.TryGetNotice("labor_shortage", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Labor, notice.PressureType);
                StringAssert.Contains("required inactive", notice.Detail);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ServiceNoticeAppearsWhenNoDoctorPracticeIsActive()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager shared = CreateComponent<SharedBusinessRuntimeManager>("Service Notice Shared Runtime", cleanup);
                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, shared, null, null, null);

                Assert.IsTrue(manager.TryGetNotice("service_gap", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Service, notice.PressureType);
                StringAssert.Contains("no doctor", notice.Detail);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void SchoolhouseNeedNoticeAppearsWhenFamilyBaseJustifiesCivicAction()
        {
            List<Object> cleanup = new();
            try
            {
                PopulationManager population = CreateComponent<PopulationManager>("Schoolhouse Notice Population", cleanup);
                AddSchoolhousePopulation(population, 28, 118, 14, 24);

                CivicFoundationManager civic = CreateComponent<CivicFoundationManager>("Schoolhouse Notice Civic", cleanup);
                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, population, null, null, null, null, null);
                SetPrivateField(manager, "civicFoundation", civic);
                manager.RefreshNotices();

                Assert.IsTrue(manager.TryGetNotice("schoolhouse_civic_need", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Service, notice.PressureType);
                StringAssert.Contains("14 school-age", notice.Detail);
                StringAssert.Contains("Schoolhouse", notice.ActionText);
                Assert.AreEqual("Civic", notice.DeskTarget);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void SchoolhouseTeacherVacancyNoticeAppearsForBuiltUnstaffedSchoolhouse()
        {
            List<Object> cleanup = new();
            try
            {
                PopulationManager population = CreateComponent<PopulationManager>("Schoolhouse Vacancy Population", cleanup);
                AddSchoolhousePopulation(population, 28, 118, 14, 24, includeAvailableTeacher: false);

                CivicFoundationManager civic = CreateComponent<CivicFoundationManager>("Schoolhouse Vacancy Civic", cleanup);
                civic.Schoolhouse.MarkBuilt(17, 4);

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, population, null, null, null, null, null);
                SetPrivateField(manager, "civicFoundation", civic);
                manager.RefreshNotices();

                Assert.IsTrue(manager.TryGetNotice("schoolhouse_teacher_vacancy", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Service, notice.PressureType);
                StringAssert.Contains("no teacher", notice.Detail.ToLowerInvariant());
                StringAssert.Contains("Hire", notice.ActionText);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ServiceNoticeBecomesPersistentWhenPressureRepeats()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager shared = CreateComponent<SharedBusinessRuntimeManager>("Persistent Service Notice Shared Runtime", cleanup);
                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, shared, null, null, null);

                manager.RefreshNotices();
                manager.RefreshNotices();
                manager.RefreshNotices();

                Assert.IsTrue(manager.TryGetNotice("service_gap", out OpportunityNotice notice));
                Assert.GreaterOrEqual(notice.ConsecutiveCycles, 3);
                Assert.AreEqual("Persistent", notice.LifecycleLabel);
                StringAssert.Contains("Persistent", manager.BuildNoticeBoardText());
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void NoticeBoardRenderingDoesNotAdvanceLifecycleState()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager shared = CreateComponent<SharedBusinessRuntimeManager>("Board Read Only Service Runtime", cleanup);
                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, shared, null, null, null);

                Assert.IsTrue(manager.TryGetNotice("service_gap", out OpportunityNotice initial));
                Assert.AreEqual("New", initial.LifecycleLabel);
                Assert.AreEqual(1, initial.ConsecutiveCycles);

                manager.BuildNoticeBoardText();
                manager.BuildNoticeBoardText();
                manager.BuildNoticeBoardText();

                Assert.IsTrue(manager.TryGetNotice("service_gap", out OpportunityNotice afterReads));
                Assert.AreEqual("New", afterReads.LifecycleLabel);
                Assert.AreEqual(1, afterReads.ConsecutiveCycles);

                manager.RefreshNotices();

                Assert.IsTrue(manager.TryGetNotice("service_gap", out OpportunityNotice afterRefresh));
                Assert.AreEqual("Watching", afterRefresh.LifecycleLabel);
                Assert.AreEqual(2, afterRefresh.ConsecutiveCycles);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void DeskSummaryRenderingDoesNotAdvanceLifecycleState()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager shared = CreateComponent<SharedBusinessRuntimeManager>("Desk Read Only Service Runtime", cleanup);
                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, shared, null, null, null);

                Assert.IsTrue(manager.TryGetNotice("service_gap", out OpportunityNotice initial));
                Assert.AreEqual("New", initial.LifecycleLabel);
                Assert.AreEqual(1, initial.ConsecutiveCycles);

                manager.BuildDeskSummary("Acquisitions");
                manager.BuildDeskSummary("Acquisitions");
                manager.BuildDeskSummary("Acquisitions");

                Assert.IsTrue(manager.TryGetNotice("service_gap", out OpportunityNotice afterReads));
                Assert.AreEqual("New", afterReads.LifecycleLabel);
                Assert.AreEqual(1, afterReads.ConsecutiveCycles);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void RuntimeStateCaptureDoesNotSynthesizeUninitializedNoticeState()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager shared = CreateComponent<SharedBusinessRuntimeManager>("Save Read Only Service Runtime", cleanup);
                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, shared, null, null, null);

                manager.ClearRuntimeState();

                OpportunityPressureRuntimeState state = manager.CaptureRuntimeState();

                Assert.IsNotNull(state);
                Assert.IsFalse(state.snapshotInitialized);
                Assert.IsEmpty(state.activeNotices);
                Assert.IsEmpty(state.noticeMemory);
                Assert.AreEqual(0, state.refreshSerial);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void RuntimeStateCaptureAndRestorePreservesNoticeMemory()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager shared = CreateComponent<SharedBusinessRuntimeManager>("Persisted Service Notice Shared Runtime", cleanup);
                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, shared, null, null, null);

                manager.RefreshNotices();
                manager.RefreshNotices();

                Assert.IsTrue(manager.TryGetNotice("service_gap", out OpportunityNotice persistentNotice));
                Assert.AreEqual("Persistent", persistentNotice.LifecycleLabel);
                Assert.GreaterOrEqual(persistentNotice.ConsecutiveCycles, 3);

                OpportunityPressureRuntimeState state = manager.CaptureRuntimeState();
                Assert.IsNotNull(state);
                Assert.IsTrue(state.snapshotInitialized);
                Assert.IsNotEmpty(state.activeNotices);
                Assert.IsNotEmpty(state.noticeMemory);

                OpportunityPressureRuntimeManager restored = CreateManager(cleanup);
                restored.Configure(null, null, null, shared, null, null, null);
                restored.RestoreRuntimeState(state);

                Assert.IsTrue(restored.TryGetNotice("service_gap", out OpportunityNotice restoredNotice));
                Assert.AreEqual("Persistent", restoredNotice.LifecycleLabel);
                Assert.AreEqual(persistentNotice.ConsecutiveCycles, restoredNotice.ConsecutiveCycles);

                restored.RefreshNotices();

                Assert.IsTrue(restored.TryGetNotice("service_gap", out OpportunityNotice continuedNotice));
                Assert.Greater(continuedNotice.ConsecutiveCycles, restoredNotice.ConsecutiveCycles);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void RentingWaitlistNoticeTargetsRentingDesk()
        {
            List<Object> cleanup = new();
            try
            {
                PopulationManager population = CreateComponent<PopulationManager>("Renting Waitlist Population", cleanup);
                population.State.rentalApplicantCount = 6;
                population.State.strongerHousingNeedCount = 5;
                population.State.rentalVacancyCount = 0;
                population.State.transientPersonCount = 3;
                population.State.rentalPressure01 = 1f;

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, population, null, null, null, null, null);

                Assert.IsTrue(manager.TryGetNotice("renting_waitlist_pressure", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Housing, notice.PressureType);
                Assert.AreEqual("Renting & Boarding", notice.DeskTarget);
                StringAssert.Contains("waitlist", notice.Title.ToLowerInvariant());
                StringAssert.Contains("Desk: Renting & Boarding", manager.BuildNoticeBoardText());
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void NoticeBoardCompactsOverlappingRentingPressureButPreservesUnderlyingNotices()
        {
            List<Object> cleanup = new();
            try
            {
                PopulationManager population = CreateComponent<PopulationManager>("Compacted Renting Notice Population", cleanup);
                population.State.rentalApplicantCount = 6;
                population.State.strongerHousingNeedCount = 5;
                population.State.rentalVacancyCount = 0;
                population.State.transientPersonCount = 3;
                population.State.rentalPressure01 = 1f;

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, population, null, null, null, null, null);

                Assert.IsTrue(manager.TryGetNotice("immigration_housing_pressure", out _));
                Assert.IsTrue(manager.TryGetNotice("renting_waitlist_pressure", out _));

                string board = manager.BuildNoticeBoardText(6);
                StringAssert.Contains("Renting waitlist pressure", board);
                Assert.IsFalse(board.Contains("Immigration housing pressure"), board);
                StringAssert.Contains("Housing shortage", board);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ExpansionNoticeRequiresPressureAndAvailableSite()
        {
            List<Object> cleanup = new();
            try
            {
                TownPulseRuntimeManager pulse = CreateComponent<TownPulseRuntimeManager>("Expansion Pulse Source", cleanup);
                pulse.BeginDailyResolution(4);
                pulse.RecordDailyFulfillment("tools_hardware", 3, 0);
                pulse.CompleteDailyResolution(4);

                TownWorldController townWorld = CreateComponent<TownWorldController>("Expansion Notice Town", cleanup);
                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(townWorld, null, null, null, pulse, null, null);

                Assert.IsFalse(manager.TryGetNotice("expansion_window", out _));

                AddVacantPlots(townWorld, 3);
                manager.RefreshNotices();

                Assert.IsTrue(manager.TryGetNotice("expansion_window", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Expansion, notice.PressureType);
                StringAssert.Contains("3 empty plots", notice.Detail);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ActiveTownActionNoticeSurfacesAcquisitionMarketActionSummary()
        {
            List<Object> cleanup = new();
            try
            {
                TownPulseRuntimeManager pulse = CreateComponent<TownPulseRuntimeManager>("Town Action Pulse Source", cleanup);
                pulse.BeginDailyResolution(4);
                pulse.RecordDailyFulfillment("tools_hardware", 4, 0);
                pulse.CompleteDailyResolution(4);

                AcquisitionMarketManager acquisition = CreateComponent<AcquisitionMarketManager>("Town Action Acquisition Market", cleanup);
                SetPrivateField(acquisition, "lastTownActionSummary", "Town action: housing pressure pushed new residential parcels toward market.");

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, null, pulse, null, null, acquisition);

                Assert.IsTrue(manager.TryGetNotice("active_town_action", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Expansion, notice.PressureType);
                StringAssert.Contains("housing pressure", notice.Detail);
                StringAssert.Contains("Review open listings", notice.ActionText);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void OpenListingsSurfaceAsPropertyOpeningLeads()
        {
            List<Object> cleanup = new();
            try
            {
                AcquisitionMarketManager acquisition = CreateComponent<AcquisitionMarketManager>("Property Opening Acquisition Market", cleanup);
                SetAcquisitionListings(
                    acquisition,
                    new AcquisitionListing
                    {
                        kind = AcquisitionListingKind.Land,
                        listingId = "plot_012",
                        title = "Front Street lot",
                        askingPriceCents = 32500,
                        plotId = 12,
                        pressure01 = 0.68f,
                        source = AcquisitionListingSource.PressureRelease,
                        sourceReason = "housing pressure released a buildable town lot"
                    });

                OpportunityPressureRuntimeManager manager = CreateManager(cleanup);
                manager.Configure(null, null, null, null, null, null, null, acquisition);

                Assert.IsTrue(manager.TryGetNotice("property_opening_land", out OpportunityNotice notice));
                Assert.AreEqual(OpportunityPressureType.Expansion, notice.PressureType);
                StringAssert.Contains("Front Street lot", notice.Detail);
                StringAssert.Contains("$325", notice.Detail);
                StringAssert.Contains("Inspect", notice.ActionText);
                Assert.AreEqual("Acquisitions", notice.DeskTarget);
                StringAssert.Contains("Property Openings", manager.BuildNoticeBoardText());
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void HudCombinesTownPulseAndOpportunityAlertText()
        {
            GameObject hudObject = new("Opportunity HUD Test");
            GameObject stripObject = new("HUD_AlertStrip", typeof(RectTransform));
            try
            {
                stripObject.transform.SetParent(hudObject.transform, false);
                LandLedgersHUDController hud = hudObject.AddComponent<LandLedgersHUDController>();
                SetPrivateField(hud, "alertStrip", stripObject.GetComponent<RectTransform>());

                hud.SetTownPulseAlert("Town Pulse: Repair Rush");
                hud.SetOpportunityNoticeAlert("Opportunities: Retail: Restock thin categories");

                Assert.IsTrue(hud.IsTownPulseAlertVisible);
                Assert.IsTrue(hud.IsOpportunityNoticeAlertVisible);
                StringAssert.Contains("Town Pulse: Repair Rush", hud.CurrentCombinedAlertText);
                StringAssert.Contains("Opportunities: Retail", hud.CurrentCombinedAlertText);
                Assert.AreEqual("Town Pulse: Repair Rush", hud.CurrentTownPulseAlertText);

                hud.SetTownPulseAlert(string.Empty);

                Assert.IsFalse(hud.IsTownPulseAlertVisible);
                Assert.IsTrue(hud.IsOpportunityNoticeAlertVisible);
                Assert.AreEqual("Opportunities: Retail: Restock thin categories", hud.CurrentCombinedAlertText);
            }
            finally
            {
                Object.DestroyImmediate(stripObject);
                Object.DestroyImmediate(hudObject);
            }
        }

        private static OpportunityPressureRuntimeManager CreateManager(List<Object> cleanup)
        {
            return CreateComponent<OpportunityPressureRuntimeManager>("Opportunity Pressure Test Runtime", cleanup);
        }

        private static T CreateComponent<T>(string name, List<Object> cleanup) where T : Component
        {
            GameObject gameObject = new(name);
            cleanup.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        private static void AddResidentialBuildings(TownWorldController townWorld, int count, List<Object> cleanup)
        {
            BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            cleanup.Add(definition);
            definition.ConfigureRuntimeFallback(
                "notice_test_home",
                "Notice Test Home",
                PlotZone.Residential,
                new Vector2Int(4, 4),
                Color.white,
                4f);

            List<PlacedBuilding> buildings = GetPrivateList<PlacedBuilding>(townWorld, "buildings");
            List<TownPlot> plots = GetPrivateList<TownPlot>(townWorld, "plots");
            for (int i = 0; i < count; i++)
            {
                plots.Add(new TownPlot
                {
                    id = i,
                    zone = PlotZone.Residential,
                    buildingId = i,
                    siteSizeCells = new Vector2Int(4, 4)
                });
                buildings.Add(new PlacedBuilding
                {
                    id = i,
                    plotId = i,
                    definition = definition,
                    playerOwned = false
                });
            }
        }

        private static void AddVacantPlots(TownWorldController townWorld, int count)
        {
            List<TownPlot> plots = GetPrivateList<TownPlot>(townWorld, "plots");
            int start = plots.Count;
            for (int i = 0; i < count; i++)
            {
                plots.Add(new TownPlot
                {
                    id = start + i,
                    zone = PlotZone.MixedUse,
                    buildingId = -1,
                    reservedForLandSale = false,
                    siteSizeCells = new Vector2Int(5, 5),
                    candidateFootprint = new GridRect(0, 0, 5, 5)
                });
            }
        }

        private static void AddHouseholds(PopulationManager population, int count)
        {
            for (int i = 0; i < count; i++)
            {
                population.State.households.Add(new HouseholdState
                {
                    id = i,
                    householdName = $"Household {i}",
                    surname = $"Household{i}",
                    homeBuildingId = i,
                    weeklyIncomeSnapshot = 1000
                });
            }
        }

        private static void AddSchoolhousePopulation(
            PopulationManager population,
            int households,
            int people,
            int schoolAgeChildren,
            int stableHouseholds,
            bool includeAvailableTeacher = true)
        {
            int personId = 0;
            for (int householdIndex = 0; householdIndex < households; householdIndex++)
            {
                HouseholdState household = new()
                {
                    id = householdIndex,
                    householdName = $"Schoolhouse Notice Household {householdIndex}",
                    surname = $"Family{householdIndex}",
                    homeBuildingId = householdIndex,
                    settlementArrangement = householdIndex < stableHouseholds
                        ? SettlementArrangement.StableHousehold
                        : SettlementArrangement.Boarding
                };
                population.State.households.Add(household);
            }

            for (int i = 0; i < people; i++)
            {
                bool child = i < schoolAgeChildren;
                HouseholdState household = population.State.households[i % population.State.households.Count];
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
                    workplaceBuildingId = includeAvailableTeacher || child ? -1 : i
                };
                population.State.people.Add(person);
                household.memberIds.Add(person.id);
            }
        }

        private static void SetSharedBusinesses(SharedBusinessRuntimeManager runtime, params BusinessInstanceState[] businesses)
        {
            List<BusinessInstanceState> list = GetPrivateList<BusinessInstanceState>(runtime, "businesses");
            list.Clear();
            for (int i = 0; i < businesses.Length; i++)
            {
                if (businesses[i] != null)
                {
                    list.Add(businesses[i]);
                }
            }
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

            Assert.Fail($"Expected profile for {businessType}.");
            return null;
        }

        private static List<T> GetPrivateList<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"{target.GetType().Name}.{fieldName} should exist.");
            List<T> list = field.GetValue(target) as List<T>;
            Assert.NotNull(list, $"{target.GetType().Name}.{fieldName} should be a List<{typeof(T).Name}>.");
            return list;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"{target.GetType().Name}.{fieldName} should exist.");
            field.SetValue(target, value);
        }

        private static string InvokeTownPulseAlertText(TownPulseRuntimeManager pulse, int absoluteDayIndex)
        {
            MethodInfo method = typeof(TownPulseRuntimeManager).GetMethod("BuildAlertText", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, "TownPulseRuntimeManager.BuildAlertText should exist.");
            return method.Invoke(pulse, new object[] { absoluteDayIndex }) as string ?? string.Empty;
        }

        private static void SetAcquisitionListings(AcquisitionMarketManager runtime, params AcquisitionListing[] listings)
        {
            List<AcquisitionListing> land = GetPrivateList<AcquisitionListing>(runtime, "landListings");
            land.Clear();
            List<AcquisitionListing> business = GetPrivateList<AcquisitionListing>(runtime, "businessListings");
            business.Clear();
            for (int i = 0; i < listings.Length; i++)
            {
                AcquisitionListing listing = listings[i];
                if (listing == null)
                {
                    continue;
                }

                if (listing.kind == AcquisitionListingKind.Business)
                {
                    business.Add(listing);
                }
                else
                {
                    land.Add(listing);
                }
            }
        }

        private static void DestroyAll(List<Object> cleanup)
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
            {
                if (cleanup[i] != null)
                {
                    Object.DestroyImmediate(cleanup[i]);
                }
            }
        }
    }
}
