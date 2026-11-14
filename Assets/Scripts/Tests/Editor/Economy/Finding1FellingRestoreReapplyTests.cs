using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// A2 plan 29 (note-112 Finding 1 follow-up, note 118): the restore
    /// contract the re-run relies on. The production change lives in
    /// harness-absent files (FirstLedgerSliceBootstrapper,
    /// SimulationDrivers), so these tests pin the contract on real
    /// registry / camp / ledger objects: the wipe empties the registry and
    /// the roster but keeps camp identity, re-registration restores the
    /// drive's prerequisites, re-running the sequence over populated state
    /// is a no-op, a post-slice round trip does not trigger, and provisions
    /// survive the wipe. The trigger predicate itself is the real
    /// harness-compiled method LoggingStandRegistry.NeedsFellingConfigReapply
    /// (note 118 C2) — no test-side mirror copy.
    /// </summary>
    [TestFixture]
    public sealed class Finding1FellingRestoreReapplyTests
    {
        private const string Holder = "holder-1";
        private const string Grantor = "town-land-office";
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

        /// <summary>
        /// The re-registration sequence ConfigureFellingCampAndRights runs
        /// for one stand: register, grant, roster two loggers. The mill /
        /// config / marker halves are the bootstrapper's, source-reviewed.
        /// </summary>
        private static void ReregisterStandAndCrew(LoggingStandRegistry registry, LoggingCampSite camp,
            string standId, List<string> diag)
        {
            Assert.IsNull(registry.RegisterStand(AuthoredStand(standId, 10)));
            Assert.IsNull(registry.GrantTimberRights(standId, Holder, Day, Grantor));
            Assert.IsNull(camp.AssignWorker(EntityId.For(EntityKind.Person, 1),
                LoggingCampRoles.Logger, Day, diag));
            Assert.IsNull(camp.AssignWorker(EntityId.For(EntityKind.Person, 2),
                LoggingCampRoles.Logger, Day, diag));
        }

        [Test]
        public void T1_WipeEmptiesRegistry_AndTriggerFires()
        {
            var registry = RegistryWithStand("stand-1", 10);
            Assert.AreEqual(1, registry.StandCount);
            Assert.AreEqual(Holder, registry.CurrentRightsHolder("stand-1"));

            registry.LoadFromSaveDto(null);
            Assert.AreEqual(0, registry.StandCount);
            Assert.AreEqual(string.Empty, registry.CurrentRightsHolder("stand-1"));

            Assert.IsTrue(registry.NeedsFellingConfigReapply(1));
            Assert.IsFalse(registry.NeedsFellingConfigReapply(0));
        }

        [Test]
        public void T2_WipeEmptiesRoster_ButKeepsCampIdentity()
        {
            var diag = new List<string>();
            var camp = CampWithCrew("camp-stand-1", "stand-1", 2, diag);
            Assert.AreEqual(2, camp.CrewHeadcount);
            Assert.AreEqual(2, camp.LoggerCount);

            // Seed one work day through the save DTO, the way the drive records it.
            var dto = camp.CaptureSaveDto();
            dto.WorkDays.Add(new LoggingCampWorkDay
            {
                DayIndex = Day,
                MonthIndex = 6,
                CrewHeadcount = 2,
                LoggerCount = 2,
                WorkableMinutes = 600,
                LogsWanted = 6,
                LogsFelled = 6,
                ProvisionsConsumed = 2f,
                LimitReason = "full-request",
            });
            camp.LoadFromSaveDto(dto);
            Assert.AreEqual(1, camp.WorkDays.Count);

            camp.LoadFromSaveDto(null);
            Assert.AreEqual(0, camp.CrewHeadcount);
            Assert.AreEqual(0, camp.WorkDays.Count);
            Assert.AreEqual("camp-stand-1", camp.CampId);
            Assert.AreEqual("biz-1", camp.BusinessInstanceId);
            Assert.AreEqual("stand-1", camp.StandId);
        }

        [Test]
        public void T3_ReregistrationAfterWipe_RestoresDrivePrerequisites()
        {
            var diag = new List<string>();
            var registry = RegistryWithStand("stand-1", 10);
            var camp = CampWithCrew("camp-stand-1", "stand-1", 2, diag);

            registry.LoadFromSaveDto(null);
            camp.LoadFromSaveDto(null);
            Assert.AreEqual(0, registry.StandCount);
            Assert.AreEqual(0, camp.CrewHeadcount);

            ReregisterStandAndCrew(registry, camp, "stand-1", diag);
            Assert.AreEqual(1, registry.StandCount);
            Assert.AreEqual(Holder, registry.CurrentRightsHolder("stand-1"));
            Assert.AreEqual(2, camp.LoggerCount);
        }

        [Test]
        public void T4_RerunOverPopulatedState_IsANoOp()
        {
            var diag = new List<string>();
            var registry = RegistryWithStand("stand-1", 10);
            var camp = CampWithCrew("camp-stand-1", "stand-1", 2, diag);

            string standRefusal = registry.RegisterStand(AuthoredStand("stand-1", 10));
            Assert.IsTrue(!string.IsNullOrEmpty(standRefusal));
            string rightsRefusal = registry.GrantTimberRights("stand-1", Holder, Day, Grantor);
            Assert.IsTrue(!string.IsNullOrEmpty(rightsRefusal));
            Assert.AreEqual("duplicate-assignment",
                camp.AssignWorker(EntityId.For(EntityKind.Person, 1),
                    LoggingCampRoles.Logger, Day, diag));

            Assert.AreEqual(1, registry.StandCount);
            Assert.AreEqual(Holder, registry.CurrentRightsHolder("stand-1"));
            Assert.AreEqual(2, camp.CrewHeadcount);
        }

        [Test]
        public void T5_PostSliceRoundTrip_DoesNotTrigger()
        {
            var diag = new List<string>();
            var registry = RegistryWithStand("stand-1", 10);
            var camp = CampWithCrew("camp-stand-1", "stand-1", 2, diag);

            LoggingStandRegistry.LoggingStandRegistrySaveDto registryDto = registry.CaptureSaveDto();
            registry.LoadFromSaveDto(registryDto);
            LoggingCampSite.LoggingCampSiteSaveDto campDto = camp.CaptureSaveDto();
            camp.LoadFromSaveDto(campDto);

            Assert.AreEqual(1, registry.StandCount);
            Assert.AreEqual(Holder, registry.CurrentRightsHolder("stand-1"));
            Assert.AreEqual(2, camp.CrewHeadcount);
            Assert.IsFalse(registry.NeedsFellingConfigReapply(1));
        }

        [Test]
        public void T6_ProvisionsSurviveTheWipe()
        {
            var diag = new List<string>();
            var camp = CampWithCrew("camp-stand-1", "stand-1", 2, diag);
            Assert.IsNull(camp.SupplyLedger.RecordDelivery(
                LoggingCampSupplyLedger.BootstrapEndowmentSupplierId, 60f, Day,
                "test endowment", diag));
            Assert.AreEqual(60f, camp.SupplyLedger.Balance);

            camp.LoadFromSaveDto(null);
            Assert.AreEqual(60f, camp.SupplyLedger.Balance);
            Assert.AreEqual(1, camp.SupplyLedger.Deliveries.Count);
        }
    }
}
