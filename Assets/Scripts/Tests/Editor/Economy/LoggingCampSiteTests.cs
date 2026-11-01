using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2D: the logging camp — crew roster, daylight-limited working day,
    /// provision supply with named provenance, landing bottleneck, and camp
    /// lifecycle (strike / relocate). Every test enforces the
    /// upstream-provenance doctrine: unfed crews and full landings are refused
    /// loudly, never auto-supplied or silently overfilled; felled lots still
    /// carry stand provenance through the W4C gates.
    /// </summary>
    [TestFixture]
    public sealed class LoggingCampSiteTests
    {
        private const string Holder = "holder-1";
        private const string Grantor = "town-land-office";

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

        private static LoggingCampSite CampWithCrew(string campId, string standId,
            EntityIdRegistry ids, int loggerCount, List<string> diag)
        {
            var camp = new LoggingCampSite(campId, "biz-1", standId, 300);
            for (int i = 0; i < loggerCount; i++)
                Assert.IsNull(camp.AssignWorker(EntityId.For(EntityKind.Person, 100 + i),
                    LoggingCampRoles.Logger, 300, diag));
            return camp;
        }

        private static void FeedCamp(LoggingCampSite camp, float units, List<string> diag)
        {
            Assert.IsNull(camp.SupplyLedger.RecordDelivery("general-store-1", units, 300,
                "weekly pack-train", diag));
        }

        [Test]
        public void Camp_RosterAssignment_DuplicatesAndBlanksRefused_BunkStrainRecordedNotWalled()
        {
            var diag = new List<string>();
            var camp = new LoggingCampSite("camp-1", "biz-1", "stand-1", 300);
            camp.Parameters.BunkCapacity = 1;
            var workerA = EntityId.For(EntityKind.Person, 1);
            var workerB = EntityId.For(EntityKind.Person, 2);

            Assert.IsNull(camp.AssignWorker(workerA, LoggingCampRoles.Logger, 300, diag));
            Assert.IsNull(camp.AssignWorker(workerB, LoggingCampRoles.SkidWorker, 300, diag),
                "Bunkhouse over-capacity is strain, not a wall — the assignment still stands.");
            Assert.AreEqual(2, camp.CrewHeadcount);
            Assert.IsTrue(diag.Exists(d => d.Contains("BUNKHOUSE STRAIN")),
                "Over-capacity must be recorded loudly.");

            Assert.IsNotNull(camp.AssignWorker(workerA, LoggingCampRoles.Logger, 300, diag),
                "Duplicate assignments are refused.");
            Assert.IsNotNull(camp.AssignWorker(EntityId.For(EntityKind.Person, 3), "  ", 300, diag),
                "Blank roles are refused.");
            Assert.IsNotNull(camp.AssignWorker(EntityId.Invalid, LoggingCampRoles.Logger, 300, diag),
                "Unnamed workers are refused.");
            Assert.AreEqual(2, camp.CrewHeadcount, "Refused assignments touch nothing.");

            Assert.AreEqual(1, camp.LoggerCount, "Only logger/feller roles count toward felling teams.");
            Assert.IsNull(camp.ReleaseWorker(workerB, diag));
            Assert.IsNotNull(camp.ReleaseWorker(workerB, diag), "Releasing twice is refused.");
            Assert.AreEqual(1, camp.CrewHeadcount);
        }

        [Test]
        public void Camp_SupplyDelivery_AnonymousRefused_BootstrapEndowmentNamedExplicitly()
        {
            var diag = new List<string>();
            var camp = new LoggingCampSite("camp-1", "biz-1", "stand-1", 300);

            Assert.IsNotNull(camp.SupplyLedger.RecordDelivery("", 10f, 300, null, diag),
                "Anonymous provisions are refused loudly.");
            Assert.IsNotNull(camp.SupplyLedger.RecordDelivery("general-store-1", 0f, 300, null, diag),
                "Non-positive deliveries are refused.");
            Assert.AreEqual(0f, camp.SupplyLedger.Balance, "Refused deliveries touch nothing.");

            Assert.IsNull(camp.SupplyLedger.RecordDelivery(
                LoggingCampSupplyLedger.BootstrapEndowmentSupplierId, 6f, 300,
                "camp opens with a grubstake", diag));
            Assert.AreEqual(6f, camp.SupplyLedger.Balance);
            Assert.AreEqual(1, camp.SupplyLedger.Deliveries.Count);
            Assert.AreEqual(LoggingCampSupplyLedger.BootstrapEndowmentSupplierId,
                camp.SupplyLedger.Deliveries[0].SupplierId,
                "A bootstrap is an explicit named endowment, never an anonymous arrival.");
        }

        [Test]
        public void Camp_CampDay_UnfedCrewRefusedLoudly_NothingFelledNothingConsumed()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", ids, 2, diag);
            // No provisions delivered: the crew goes unfed.

            LogLot lot = camp.RunCampDay(registry, operation, Holder, 10,
                EntityId.For(EntityKind.Person, 1), 300, 6, diag);

            Assert.IsNull(lot, "An unfed crew is refused loudly — never auto-supplied.");
            Assert.AreEqual(100, registry.GetStand("stand-1").StandingTimberUnits,
                "A refused day fells nothing.");
            Assert.AreEqual(0, operation.LandingLogUnits);
            Assert.AreEqual(0, camp.WorkDays.Count, "A refused day is not recorded as a work day.");
        }

        [Test]
        public void Camp_CampDay_FedCrewFellsFullRequest_WithinDaylightCapacity()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", ids, 2, diag);
            FeedCamp(camp, 10f, diag);

            // June: 894 usable minutes; one two-person team; 894/90 = 9 logs/team capacity.
            LogLot lot = camp.RunCampDay(registry, operation, Holder, 5,
                EntityId.For(EntityKind.Person, 1), 300, 6, diag);

            Assert.IsNotNull(lot);
            Assert.AreEqual(5, lot.LogUnits);
            Assert.AreEqual("stand-1", lot.StandId, "Stand provenance survives the camp gate.");
            Assert.AreEqual(95, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(5, operation.LandingLogUnits);
            Assert.AreEqual(8f, camp.SupplyLedger.Balance, "Two heads x one ration consumed.");
            Assert.AreEqual(1, camp.WorkDays.Count);
            Assert.AreEqual("full-request", camp.WorkDays[0].LimitReason);
            Assert.AreEqual(894, camp.WorkDays[0].WorkableMinutes);
        }

        [Test]
        public void Camp_CampDay_PartialDayWhenWantedExceedsCapacity_RecordsCrewBound()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", ids, 2, diag);
            FeedCamp(camp, 10f, diag);

            // December: 492 usable minutes; 492/90 = 5 logs for the single team.
            LogLot lot = camp.RunCampDay(registry, operation, Holder, 50,
                EntityId.For(EntityKind.Person, 1), 300, 12, diag);

            Assert.IsNotNull(lot);
            Assert.AreEqual(5, lot.LogUnits, "The day fells what the daylight allows — no more.");
            Assert.AreEqual("crew", camp.WorkDays[0].LimitReason);
            Assert.AreEqual(492, camp.WorkDays[0].WorkableMinutes);
        }

        [Test]
        public void Camp_CampDay_LandingFullRefusedLoudly_BottleneckDoctrine()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            Assert.IsNull(operation.SetLandingCapacity(10, diag));
            var camp = CampWithCrew("camp-1", "stand-1", ids, 2, diag);
            FeedCamp(camp, 40f, diag);

            // Day 1: crew cap 9 (June) fits in the 10-log landing.
            Assert.IsNotNull(camp.RunCampDay(registry, operation, Holder, 20,
                EntityId.For(EntityKind.Person, 1), 300, 6, diag));
            // Day 2: one log of room left.
            Assert.IsNotNull(camp.RunCampDay(registry, operation, Holder, 20,
                EntityId.For(EntityKind.Person, 1), 301, 6, diag));
            Assert.AreEqual(10, operation.LandingLogUnits);
            // Day 3: the landing is full — felling is refused, provisions untouched.
            float balanceBefore = camp.SupplyLedger.Balance;
            LogLot refused = camp.RunCampDay(registry, operation, Holder, 20,
                EntityId.For(EntityKind.Person, 1), 302, 6, diag);
            Assert.IsNull(refused, "A full landing refuses felling loudly — haul logs out first.");
            Assert.AreEqual(balanceBefore, camp.SupplyLedger.Balance,
                "A refused day consumes no provisions.");
            Assert.AreEqual(90, registry.GetStand("stand-1").StandingTimberUnits,
                "A refused day fells no timber.");
        }

        [Test]
        public void Camp_CampDay_RightsViolationRefused_ProvisionsNotConsumed()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", ids, 2, diag);
            FeedCamp(camp, 10f, diag);

            LogLot lot = camp.RunCampDay(registry, operation, "timber-thief", 5,
                EntityId.For(EntityKind.Person, 1), 300, 6, diag);

            Assert.IsNull(lot, "The T1E rights gate still refuses through the camp path.");
            Assert.AreEqual(10f, camp.SupplyLedger.Balance, "A refused day consumes no provisions.");
            Assert.AreEqual(100, registry.GetStand("stand-1").StandingTimberUnits);
        }

        [Test]
        public void Camp_CampDay_NoFellingCrewRefused_SingleLoggerIsNotATeam()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", ids, 1, diag); // one logger: no team
            FeedCamp(camp, 10f, diag);

            Assert.IsNull(camp.RunCampDay(registry, operation, Holder, 5,
                EntityId.For(EntityKind.Person, 101), 300, 6, diag),
                "Felling is two-person hand-tool work (T1E) — one logger is not a team.");
            Assert.AreEqual(10f, camp.SupplyLedger.Balance);
        }

        [Test]
        public void Camp_CampDay_ExhaustedStandRefused_StrikeEndsCamp()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 5);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", ids, 2, diag);
            FeedCamp(camp, 10f, diag);

            Assert.IsNotNull(camp.RunCampDay(registry, operation, Holder, 5,
                EntityId.For(EntityKind.Person, 1), 300, 6, diag));
            Assert.AreEqual(0, registry.GetStand("stand-1").StandingTimberUnits);

            Assert.IsNull(camp.RunCampDay(registry, operation, Holder, 5,
                EntityId.For(EntityKind.Person, 1), 301, 6, diag),
                "A cut-out stand refuses the camp day — strike or relocate.");

            Assert.IsNull(camp.StrikeCamp(302, "stand cut out", diag));
            Assert.IsTrue(camp.IsStruck);
            Assert.IsNotNull(camp.StrikeCamp(303, "again", diag), "Striking twice is refused.");
            Assert.IsNull(camp.RunCampDay(registry, operation, Holder, 5,
                EntityId.For(EntityKind.Person, 1), 303, 6, diag),
                "A struck camp works no more days.");
        }

        [Test]
        public void Camp_RelocateCamp_UnknownStandRefused_DownDaysEnforced()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 5);
            Assert.IsNull(registry.RegisterStand(AuthoredStand("stand-2", 50)));
            Assert.IsNull(registry.GrantTimberRights("stand-2", Holder, 200, Grantor));
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-1", "stand-1", ids, 2, diag);
            camp.Parameters.CampMoveDownDays = 2;
            FeedCamp(camp, 20f, diag);

            Assert.IsNotNull(camp.RelocateCamp("conjured-ground", registry, 300, diag),
                "Camps move to real registered stands only.");
            Assert.IsNotNull(camp.RelocateCamp("stand-1", registry, 300, diag),
                "Relocating to the current stand is refused.");
            Assert.AreEqual("stand-1", camp.StandId);

            Assert.IsNull(camp.RelocateCamp("stand-2", registry, 300, diag));
            Assert.AreEqual("stand-2", camp.StandId);
            Assert.IsNull(camp.RunCampDay(registry, operation, Holder, 5,
                EntityId.For(EntityKind.Person, 1), 301, 6, diag),
                "The camp cannot fell during parameterized relocation down days.");
            Assert.IsNotNull(camp.RunCampDay(registry, operation, Holder, 5,
                EntityId.For(EntityKind.Person, 1), 302, 6, diag),
                "After the down days the camp fells at the new stand.");
            Assert.AreEqual(45, registry.GetStand("stand-2").StandingTimberUnits);
            Assert.AreEqual(5, registry.GetStand("stand-1").StandingTimberUnits,
                "The old stand's remaining timber stays with the registry.");
        }

        [Test]
        public void DaylightTable_InvalidMonthRefused_OverrideWinsForCalibration()
        {
            try
            {
                Assert.AreEqual(-1, LoggingSeasonalDaylight.UsableWorkMinutes(0));
                Assert.AreEqual(-1, LoggingSeasonalDaylight.UsableWorkMinutes(13));
                Assert.AreEqual(894, LoggingSeasonalDaylight.UsableWorkMinutes(6));

                Assert.IsNotNull(LoggingSeasonalDaylight.OverrideMinutes(0, 600),
                    "Invalid months cannot be overridden.");
                Assert.IsNotNull(LoggingSeasonalDaylight.OverrideMinutes(6, 0),
                    "Zeroing the working day by override is refused.");
                Assert.IsNull(LoggingSeasonalDaylight.OverrideMinutes(6, 600));
                Assert.AreEqual(600, LoggingSeasonalDaylight.UsableWorkMinutes(6),
                    "Kennedy's calibration override wins over the base table.");
            }
            finally
            {
                LoggingSeasonalDaylight.ClearOverrides();
            }
            Assert.AreEqual(894, LoggingSeasonalDaylight.UsableWorkMinutes(6),
                "Clearing overrides restores the base table.");
        }

        [Test]
        public void Operation_LandingCapacity_ShrinkBelowStagedRefused_NegativeRefused()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);

            Assert.AreEqual(int.MaxValue, operation.LandingCapacityLogUnits,
                "Default is unbounded — existing behavior unchanged.");
            Assert.IsNull(operation.SetLandingCapacity(20, diag));
            Assert.AreEqual(20, operation.LandingCapacityLogUnits);

            Assert.IsNotNull(operation.RunFelling(registry, "stand-1", Holder, 15,
                EntityId.For(EntityKind.Person, 1), 300, diag, null));
            Assert.AreEqual(15, operation.LandingLogUnits);

            Assert.IsNotNull(operation.SetLandingCapacity(10, diag),
                "Shrinking below staged logs would strand them — refused.");
            Assert.IsNotNull(operation.SetLandingCapacity(-1, diag), "Negative capacity refused.");
            Assert.AreEqual(20, operation.LandingCapacityLogUnits, "Refusals touch nothing.");
            Assert.IsNull(operation.SetLandingCapacity(30, diag));
            Assert.AreEqual(30, operation.LandingCapacityLogUnits);
        }

        [Test]
        public void Camp_SaveRoundTrip_PreservesRosterProvisionsWorkDaysAndParameters()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime("biz-1", ids);
            var camp = CampWithCrew("camp-7", "stand-1", ids, 3, diag);
            camp.Parameters.CampMoveDownDays = 2;
            camp.Parameters.BunkCapacity = 4;
            FeedCamp(camp, 10f, diag);
            Assert.IsNotNull(camp.RunCampDay(registry, operation, Holder, 5,
                EntityId.For(EntityKind.Person, 100), 300, 6, diag));

            var restored = new LoggingCampSite(string.Empty, string.Empty, string.Empty, 0);
            restored.LoadFromSaveDto(camp.CaptureSaveDto());

            Assert.AreEqual("camp-7", restored.CampId);
            Assert.AreEqual("biz-1", restored.BusinessInstanceId);
            Assert.AreEqual("stand-1", restored.StandId);
            Assert.AreEqual(300, restored.DayEstablished);
            Assert.IsFalse(restored.IsStruck);
            Assert.AreEqual(3, restored.CrewHeadcount);
            Assert.AreEqual(3, restored.LoggerCount);
            Assert.AreEqual(7f, restored.SupplyLedger.Balance, "10 - 3 heads x 1 ration.");
            Assert.AreEqual(1, restored.SupplyLedger.Deliveries.Count);
            Assert.AreEqual(1, restored.WorkDays.Count);
            Assert.AreEqual(5, restored.WorkDays[0].LogsFelled);
            Assert.AreEqual(2, restored.Parameters.CampMoveDownDays);
            Assert.AreEqual(4, restored.Parameters.BunkCapacity);
        }
    }
}
