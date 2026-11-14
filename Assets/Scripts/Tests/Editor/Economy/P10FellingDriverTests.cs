using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// A2 plan 27 (P10 live felling driver, note 110): the daily felling
    /// contract the driver relies on. SimulationDrivers.DriveFelling is
    /// harness-excluded (Unity-scene-bound, like DriveSawmillProduction),
    /// so these tests pin the camp-side contract at the exact call shape
    /// the driver uses — the budget-capped logsWanted (note 110 C2), the
    /// pre-commit / release / record settlement, and the loud refusals —
    /// through DriveFellingDay, a faithful mirror of the driver's steps
    /// 2-7 (the mill/Pass 9 resolution in step 1 needs the scene and is
    /// source-reviewed). The driver itself is source-reviewed.
    /// </summary>
    [TestFixture]
    public sealed class P10FellingDriverTests
    {
        private const string Holder = "holder-1";
        private const string Grantor = "town-land-office";
        private const int June = 6;
        private const int Day = 300;

        private static TimberStand AuthoredStand(string standId, int units)
        {
            return new TimberStand
            {
                StandId = standId,
                DisplayName = "Test Pines",
                LocationId = "landing-1",
                TimberRightsHolderId = string.Empty,
                Species = "white pine",
                StandingTimberUnits = units,
            };
        }

        private static LoggingStandRegistry RegistryWithStand(string standId, int units)
        {
            var registry = new LoggingStandRegistry();
            Assert.IsNull(registry.RegisterStand(AuthoredStand(standId, units)));
            Assert.IsNull(registry.GrantTimberRights(standId, Holder, 200, Grantor));
            return registry;
        }

        private static LoggingCampSite CampWithCrew(string campId, string standId, int loggerCount, List<string> diag)
        {
            var camp = new LoggingCampSite(campId, "biz-1", standId, Day);
            for (int i = 0; i < loggerCount; i++)
                Assert.IsNull(camp.AssignWorker(EntityId.For(EntityKind.Person, 1 + i),
                    LoggingCampRoles.Logger, Day, diag));
            return camp;
        }

        private static EquipmentTaskGate FellingEquipmentGate()
        {
            return new EquipmentTaskGate
            {
                AssetsSupplier = () => new List<EquipmentAsset>
                {
                    new EquipmentAsset
                    {
                        AssetId = "test-felling-axe",
                        Kind = "felling-axe",
                        OwnerKind = "business",
                        OwnerId = "biz-1",
                        Condition01 = 1f,
                    },
                },
            };
        }

        private static void FeedCamp(LoggingCampSite camp, float units, List<string> diag)
        {
            Assert.IsNull(camp.SupplyLedger.RecordDelivery(
                LoggingCampSupplyLedger.BootstrapEndowmentSupplierId, units, Day,
                "P10 opening provision endowment", diag));
        }

        /// <summary>
        /// Mirrors SimulationDrivers.DriveFelling steps 2-7: the empty-holder
        /// guard, the working-logger and budget-cap computation (note 110
        /// C2), the pre-commit, the camp day, and the budget settlement.
        /// Guard refusals are recorded in diag, mirroring the driver's
        /// once-per-key warnings.
        /// </summary>
        private static LogLot DriveFellingDay(LoggingCampSite camp, LoggingStandRegistry registry,
            LoggingOperationRuntime operation, WorkTimeBudgetStore budgets,
            int dayIndex, int monthIndex, List<string> diag, EquipmentTaskGate gate)
        {
            string rightsHolderId = registry.CurrentRightsHolder(camp.StandId);
            if (string.IsNullOrWhiteSpace(rightsHolderId))
            {
                diag.Add($"Felling skipped: no timber rights are granted on stand '{camp.StandId}'.");
                return null;
            }

            var loggers = new List<LoggingCampCrewEntry>();
            foreach (LoggingCampCrewEntry entry in camp.Crew)
            {
                if (entry == null) continue;
                if (!string.Equals(entry.Role, LoggingCampRoles.Logger, StringComparison.Ordinal)
                    && !string.Equals(entry.Role, LoggingCampRoles.Feller, StringComparison.Ordinal)) continue;
                loggers.Add(entry);
            }
            loggers.Sort((a, b) => a.DayJoined.CompareTo(b.DayJoined));
            int teams = loggers.Count / 2;
            if (teams <= 0)
            {
                diag.Add($"Felling skipped: logging camp '{camp.CampId}' has no two-person felling crew rostered.");
                return null;
            }
            var workingLoggers = new List<EntityId>();
            for (int i = 0; i < teams * 2; i++) workingLoggers.Add(loggers[i].Worker);

            int budgetCap = int.MaxValue;
            EntityId exhaustedLogger = EntityId.Invalid;
            int lowestRemaining = int.MaxValue;
            foreach (EntityId logger in workingLoggers)
            {
                int remaining = budgets.GetOrCreate(logger).MinutesRemaining;
                if (remaining < lowestRemaining) { lowestRemaining = remaining; exhaustedLogger = logger; }
                int cap = remaining / TimberHarvest.FellMinutesPerLog;
                if (cap < budgetCap) budgetCap = cap;
            }
            if (budgetCap <= 0)
            {
                diag.Add($"Felling skipped: feller {exhaustedLogger} has only {lowestRemaining} work minutes remaining today.");
                return null;
            }
            int logsWanted = teams * budgetCap;

            var committedLoggers = new List<EntityId>();
            foreach (EntityId logger in workingLoggers)
            {
                if (!budgets.TryCommitTask(logger, budgetCap * TimberHarvest.FellMinutesPerLog, out string reason))
                {
                    foreach (EntityId committed in committedLoggers)
                        budgets.GetOrCreate(committed).ReleaseCommitment(budgetCap * TimberHarvest.FellMinutesPerLog);
                    diag.Add($"Felling skipped: work-time budget commit refused for feller {logger} ({reason}).");
                    return null;
                }
                committedLoggers.Add(logger);
            }

            LogLot lot = camp.RunCampDay(registry, operation, rightsHolderId,
                logsWanted, workingLoggers[0], dayIndex, monthIndex, diag, gate);
            if (lot == null)
            {
                foreach (EntityId logger in workingLoggers)
                    budgets.GetOrCreate(logger).ReleaseCommitment(budgetCap * TimberHarvest.FellMinutesPerLog);
                return null;
            }
            int logsPerTeam = (lot.LogUnits + teams - 1) / teams;
            int minutesPerLogger = logsPerTeam * TimberHarvest.FellMinutesPerLog;
            int releasePerLogger = (budgetCap - logsPerTeam) * TimberHarvest.FellMinutesPerLog;
            foreach (EntityId logger in workingLoggers)
            {
                if (releasePerLogger > 0)
                    budgets.GetOrCreate(logger).ReleaseCommitment(releasePerLogger);
                budgets.GetOrCreate(logger).RecordWorked(minutesPerLogger);
            }
            return lot;
        }

        [Test]
        public void T1_HappyPath_FedCrewFellsBudgetCappedRequest_WithProvenanceAndSettlement()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", 2, diag);
            FeedCamp(camp, 10f, diag);
            var budgets = new WorkTimeBudgetStore();

            LogLot lot = DriveFellingDay(camp, registry, operation, budgets, Day, June, diag, FellingEquipmentGate());

            Assert.IsNotNull(lot);
            Assert.AreEqual(6, lot.LogUnits, "June daylight allows 9 logs per team, but a feller's 600-minute day affords only 6 (note 110 C2).");
            Assert.AreEqual(Holder, lot.OwnerId);
            Assert.AreEqual("stand-1", lot.StandId);
            Assert.AreEqual(1800f, lot.MassKg, 0.01f);
            Assert.AreEqual(94, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(6, operation.LandingLogUnits);
            Assert.AreEqual(8f, camp.SupplyLedger.Balance, "Two heads x one ration consumed.");
            Assert.AreEqual(1, camp.WorkDays.Count);
            Assert.AreEqual(6, camp.WorkDays[0].LogsWanted);
            Assert.AreEqual(6, camp.WorkDays[0].LogsFelled);
            Assert.AreEqual("full-request", camp.WorkDays[0].LimitReason);
            Assert.AreEqual(894, camp.WorkDays[0].WorkableMinutes);
            Assert.AreEqual(2f, camp.WorkDays[0].ProvisionsConsumed);
            foreach (int personId in new[] { 1, 2 })
            {
                WorkTimeBudget budget = budgets.GetOrCreate(EntityId.For(EntityKind.Person, personId));
                Assert.AreEqual(540, budget.MinutesWorked, "6 logs x 90 minutes recorded per feller.");
                Assert.AreEqual(540, budget.MinutesCommitted);
                Assert.AreEqual(60, budget.MinutesRemaining, "60 minutes remain for the same feller's sawmill shift.");
            }
        }

        [Test]
        public void T2_UnfedCrewRefused_NothingFelled_BudgetReleasedInFull()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", 2, diag);
            // No provisions delivered: the crew goes unfed.
            var budgets = new WorkTimeBudgetStore();

            LogLot lot = DriveFellingDay(camp, registry, operation, budgets, Day, June, diag, FellingEquipmentGate());

            Assert.IsNull(lot, "An unfed crew is refused loudly — never auto-supplied.");
            Assert.AreEqual(100, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(0, operation.LandingLogUnits);
            Assert.AreEqual(0, camp.WorkDays.Count, "A refused day is not recorded as a work day.");
            Assert.IsTrue(diag.Exists(d => d.Contains("unfed")), "The refusal names the unfed crew loudly.");
            foreach (int personId in new[] { 1, 2 })
            {
                WorkTimeBudget budget = budgets.GetOrCreate(EntityId.For(EntityKind.Person, personId));
                Assert.AreEqual(0, budget.MinutesCommitted, "A refused day releases the full pre-commitment.");
                Assert.AreEqual(600, budget.MinutesRemaining);
                Assert.AreEqual(0, budget.MinutesWorked);
            }
        }

        [Test]
        public void T3_NoRightsRefused_NothingFelled_NothingConsumed()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = new LoggingStandRegistry();
            Assert.IsNull(registry.RegisterStand(AuthoredStand("stand-1", 100)));
            // No grant: nobody holds timber rights on this stand.
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", 2, diag);
            FeedCamp(camp, 10f, diag);
            var budgets = new WorkTimeBudgetStore();

            LogLot lot = DriveFellingDay(camp, registry, operation, budgets, Day, June, diag, FellingEquipmentGate());

            Assert.IsNull(lot, "Felling without timber rights is refused — the empty holder never passes through.");
            Assert.AreEqual(100, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(0, operation.LandingLogUnits);
            Assert.AreEqual(10f, camp.SupplyLedger.Balance, "A refused day consumes no provisions.");
            Assert.AreEqual(0, camp.WorkDays.Count);
            Assert.IsTrue(diag.Exists(d => d.Contains("no timber rights")), "The refusal names the missing rights loudly.");
            foreach (int personId in new[] { 1, 2 })
            {
                WorkTimeBudget budget = budgets.GetOrCreate(EntityId.For(EntityKind.Person, personId));
                Assert.AreEqual(0, budget.MinutesCommitted, "The guard refuses before any budget is committed.");
                Assert.AreEqual(600, budget.MinutesRemaining);
            }
        }

        [Test]
        public void T4_NullGateRefused_NothingFelled_BudgetReleasedInFull()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", 2, diag);
            FeedCamp(camp, 10f, diag);
            var budgets = new WorkTimeBudgetStore();

            LogLot lot = DriveFellingDay(camp, registry, operation, budgets, Day, June, diag, null);

            Assert.IsNull(lot, "Felling without an equipment authority is refused.");
            Assert.AreEqual(100, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(0, operation.LandingLogUnits);
            Assert.AreEqual(10f, camp.SupplyLedger.Balance, "A refused day consumes no provisions.");
            Assert.AreEqual(0, camp.WorkDays.Count);
            Assert.IsTrue(diag.Exists(d => d.Contains("no equipment authority")), "The refusal names the missing gate loudly.");
            foreach (int personId in new[] { 1, 2 })
            {
                WorkTimeBudget budget = budgets.GetOrCreate(EntityId.For(EntityKind.Person, personId));
                Assert.AreEqual(0, budget.MinutesCommitted, "A refused day releases the full pre-commitment.");
                Assert.AreEqual(600, budget.MinutesRemaining);
                Assert.AreEqual(0, budget.MinutesWorked);
            }
        }

        [Test]
        public void T5_OneDayOnce_SecondCallSameDayRefused_FirstDayStands()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", 2, diag);
            FeedCamp(camp, 10f, diag);
            var budgets = new WorkTimeBudgetStore();

            Assert.IsNotNull(DriveFellingDay(camp, registry, operation, budgets, Day, June, diag, FellingEquipmentGate()));
            Assert.AreEqual(94, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(6, operation.LandingLogUnits);

            // A fresh budget store isolates the camp's one-day-once rule
            // from the first day's spent budget.
            var freshBudgets = new WorkTimeBudgetStore();
            LogLot duplicate = DriveFellingDay(camp, registry, operation, freshBudgets, Day, June, diag, FellingEquipmentGate());

            Assert.IsNull(duplicate, "One camp cannot harvest twice for the same simulated workday.");
            Assert.AreEqual(94, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(6, operation.LandingLogUnits);
            Assert.AreEqual(8f, camp.SupplyLedger.Balance, "A duplicate workday attempt consumes no additional provisions.");
            Assert.AreEqual(1, camp.WorkDays.Count);
            foreach (int personId in new[] { 1, 2 })
            {
                WorkTimeBudget budget = freshBudgets.GetOrCreate(EntityId.For(EntityKind.Person, personId));
                Assert.AreEqual(0, budget.MinutesCommitted, "The refused duplicate releases its pre-commitment in full.");
                Assert.AreEqual(600, budget.MinutesRemaining);
            }
        }

        [Test]
        public void T6_DaylightCap_JuneNineLogsWhenBudgetAllows_TenthLogUnfelled()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", 2, diag);
            FeedCamp(camp, 10f, diag);
            var budgets = new WorkTimeBudgetStore();
            // A 900-minute day, so the budget cap (10 logs) does not bind
            // before the daylight cap: June allows 894/90 = 9 logs per team.
            budgets.GetOrCreate(EntityId.For(EntityKind.Person, 1), 900);
            budgets.GetOrCreate(EntityId.For(EntityKind.Person, 2), 900);

            LogLot lot = DriveFellingDay(camp, registry, operation, budgets, Day, June, diag, FellingEquipmentGate());

            Assert.IsNotNull(lot);
            Assert.AreEqual(9, lot.LogUnits, "The day fells what the daylight allows — no more.");
            Assert.AreEqual("crew", camp.WorkDays[0].LimitReason);
            Assert.AreEqual(894, camp.WorkDays[0].WorkableMinutes);
            Assert.AreEqual(10, camp.WorkDays[0].LogsWanted, "The caller wanted 10; the daylight bound it to 9.");
            Assert.AreEqual(91, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(9, operation.LandingLogUnits);
            foreach (int personId in new[] { 1, 2 })
            {
                WorkTimeBudget budget = budgets.GetOrCreate(EntityId.For(EntityKind.Person, personId));
                Assert.AreEqual(810, budget.MinutesWorked, "9 logs x 90 minutes recorded per feller.");
                Assert.AreEqual(810, budget.MinutesCommitted, "The 90 unworked minutes of the cap are released.");
                Assert.AreEqual(90, budget.MinutesRemaining);
            }
        }

        [Test]
        public void T7_RoundTrip_PreservesRegistryCampAndFelledDay()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-7", "stand-1", 2, diag);
            FeedCamp(camp, 10f, diag);
            var budgets = new WorkTimeBudgetStore();
            Assert.IsNotNull(DriveFellingDay(camp, registry, operation, budgets, Day, June, diag, FellingEquipmentGate()));

            var restoredRegistry = new LoggingStandRegistry();
            restoredRegistry.LoadFromSaveDto(registry.CaptureSaveDto());
            Assert.AreEqual(1, restoredRegistry.StandCount);
            Assert.AreEqual(94, restoredRegistry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(Holder, restoredRegistry.CurrentRightsHolder("stand-1"));
            Assert.AreEqual(1, restoredRegistry.RightsGrants.Count);
            Assert.AreEqual(Grantor, restoredRegistry.RightsGrants[0].GrantedBy);

            var restoredCamp = new LoggingCampSite(string.Empty, string.Empty, string.Empty, 0);
            restoredCamp.LoadFromSaveDto(camp.CaptureSaveDto());
            Assert.AreEqual("camp-7", restoredCamp.CampId);
            Assert.AreEqual("biz-1", restoredCamp.BusinessInstanceId);
            Assert.AreEqual("stand-1", restoredCamp.StandId);
            Assert.AreEqual(Day, restoredCamp.DayEstablished);
            Assert.AreEqual(2, restoredCamp.CrewHeadcount);
            Assert.AreEqual(2, restoredCamp.LoggerCount);
            Assert.AreEqual(8f, restoredCamp.SupplyLedger.Balance);
            Assert.AreEqual(1, restoredCamp.WorkDays.Count);
            Assert.AreEqual(6, restoredCamp.WorkDays[0].LogsWanted);
            Assert.AreEqual(6, restoredCamp.WorkDays[0].LogsFelled);
        }

        [Test]
        public void T8_RepeatableAcrossDays_ThreeJuneDaysFellEighteenLogs()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", 2, diag);
            FeedCamp(camp, 40f, diag);
            var budgets = new WorkTimeBudgetStore();

            budgets.EnsureDay(Day);
            Assert.IsNotNull(DriveFellingDay(camp, registry, operation, budgets, Day, June, diag, FellingEquipmentGate()));
            budgets.EnsureDay(Day + 1);
            Assert.IsNotNull(DriveFellingDay(camp, registry, operation, budgets, Day + 1, June, diag, FellingEquipmentGate()));
            budgets.EnsureDay(Day + 2);
            Assert.IsNotNull(DriveFellingDay(camp, registry, operation, budgets, Day + 2, June, diag, FellingEquipmentGate()));

            Assert.AreEqual(82, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(18, operation.LandingLogUnits);
            Assert.AreEqual(3, camp.WorkDays.Count);
            Assert.AreEqual(34f, camp.SupplyLedger.Balance, "Three days x two heads x one ration.");
            foreach (int personId in new[] { 1, 2 })
            {
                WorkTimeBudget budget = budgets.GetOrCreate(EntityId.For(EntityKind.Person, personId));
                Assert.AreEqual(540, budget.MinutesWorked, "The third day's fell is recorded; earlier days rolled over.");
                Assert.AreEqual(60, budget.MinutesRemaining);
            }
        }

        [Test]
        public void T9_CallerCapRespected_LogsWantedSixYieldsSixLogs_DespiteDaylightAndStand()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 1000);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", 2, diag);
            FeedCamp(camp, 10f, diag);
            var budgets = new WorkTimeBudgetStore();

            LogLot lot = DriveFellingDay(camp, registry, operation, budgets, Day, June, diag, FellingEquipmentGate());

            Assert.IsNotNull(lot);
            Assert.AreEqual(6, lot.LogUnits, "The caller cap (teams x budgetCap = 6) binds before the June daylight cap of 9.");
            Assert.AreEqual(1, camp.WorkDays.Count);
            Assert.AreEqual(6, camp.WorkDays[0].LogsWanted);
            Assert.AreEqual(6, camp.WorkDays[0].LogsFelled);
            Assert.AreEqual("full-request", camp.WorkDays[0].LimitReason);
            Assert.AreEqual(994, registry.GetStand("stand-1").StandingTimberUnits, "The large stand is barely touched.");
            Assert.AreEqual(6, operation.LandingLogUnits);
        }
    }
}
