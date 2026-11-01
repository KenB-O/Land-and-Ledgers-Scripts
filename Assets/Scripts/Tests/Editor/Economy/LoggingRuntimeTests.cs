using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W4C: logging below the sawmill — timber stands, felling rights, task
    /// definitions, and journey-based log transport. Every test enforces the
    /// upstream-provenance doctrine: felled logs trace to a real registered
    /// stand with real felling rights; felling without rights is refused
    /// loudly, never silently allowed; lots are never duplicated or
    /// teleported in transit.
    /// </summary>
    [TestFixture]
    public sealed class LoggingRuntimeTests
    {
        private static TimberStand AuthoredStand(string standId, int units, string holder = "")
        {
            return new TimberStand
            {
                StandId = standId,
                DisplayName = "Test Pines",
                LocationId = "landing-1",
                TimberRightsHolderId = holder,
                Species = "white pine",
                StandingTimberUnits = units,
            };
        }

        private static LoggingStandRegistry RegistryWithGrantedStand(
            string standId, string holder, int units, List<string> diagnostics)
        {
            var registry = new LoggingStandRegistry();
            Assert.IsNull(registry.RegisterStand(AuthoredStand(standId, units)));
            Assert.IsNull(registry.GrantTimberRights(standId, holder, 200, "town-land-office"));
            return registry;
        }

        private static JourneyModel LandingToMillJourney()
        {
            var model = new JourneyModel();
            Assert.IsNull(model.RegisterLocation(new JourneyLocation(
                "landing-1", JourneyLocationKind.Waypoint, "North Landing", 2f, 0f)));
            Assert.IsNull(model.RegisterLocation(new JourneyLocation(
                "mill-1-yard", JourneyLocationKind.Mill, "Local Sawmill", 0f, 0f)
                { BusinessId = "mill-1" }));
            Assert.IsNull(model.AddEdge("landing-1", "mill-1-yard", 2f, "river road"));
            return model;
        }

        [Test]
        public void StandRegistry_RegistersAuthoredStand_RefusesDuplicatesAndBlanks()
        {
            var registry = new LoggingStandRegistry();

            Assert.IsNull(registry.RegisterStand(AuthoredStand("stand-1", 10)));
            Assert.AreEqual(1, registry.StandCount);
            Assert.NotNull(registry.GetStand("stand-1"));

            Assert.NotNull(registry.RegisterStand(AuthoredStand("stand-1", 5)),
                "Stand ids are never reused.");
            Assert.NotNull(registry.RegisterStand(AuthoredStand("", 5)),
                "Stands need a stable StandId.");
            Assert.NotNull(registry.RegisterStand(null),
                "Stands are authored, never conjured.");
            Assert.AreEqual(1, registry.StandCount, "Refused registrations touch nothing.");
        }

        [Test]
        public void RightsGrant_ExplicitGrantSetsHolder_RefusesSilentOverwrite()
        {
            var registry = new LoggingStandRegistry();
            Assert.IsNull(registry.RegisterStand(AuthoredStand("stand-1", 10)));

            Assert.IsNull(registry.GrantTimberRights("stand-1", "logging-1", 200, "town-land-office"));
            Assert.AreEqual("logging-1", registry.CurrentRightsHolder("stand-1"));
            Assert.AreEqual(1, registry.RightsGrants.Count);
            TimberRightsGrant grant = registry.RightsGrants[0];
            Assert.AreEqual("stand-1", grant.StandId);
            Assert.AreEqual(200, grant.GrantDayIndex);
            Assert.AreEqual("town-land-office", grant.GrantedBy);

            // A second grant on a held stand is a conveyance — silent overwrites refused.
            Assert.NotNull(registry.GrantTimberRights("stand-1", "poacher", 210, "nobody"));
            Assert.AreEqual("logging-1", registry.CurrentRightsHolder("stand-1"));

            // Grants require a named holder, a named grantor, and a known stand.
            Assert.NotNull(registry.GrantTimberRights("stand-1", "", 210, "town-land-office"));
            Assert.NotNull(registry.GrantTimberRights("stand-1", "logging-2", 210, ""));
            Assert.NotNull(registry.GrantTimberRights("stand-nope", "logging-2", 210, "town-land-office"));
        }

        [Test]
        public void RightsTransfer_RecordedExplicitly_OldHolderLosesRights()
        {
            var diagnostics = new List<string>();
            var registry = RegistryWithGrantedStand("stand-1", "logging-1", 10, diagnostics);

            Assert.IsNull(registry.TransferTimberRights("stand-1", "mill-1", 220));
            Assert.AreEqual("mill-1", registry.CurrentRightsHolder("stand-1"));
            Assert.AreEqual(2, registry.RightsGrants.Count, "The transfer is recorded, never silent.");
            Assert.AreEqual("logging-1", registry.RightsGrants[1].GrantedBy);

            // The old holder can no longer fell; the new holder can.
            var ids = new EntityIdRegistry();
            Assert.IsNull(registry.FellLogs(ids, "stand-1", "logging-1", 2,
                EntityId.For(EntityKind.Person, 5), 221, diagnostics));
            LogLot lot = registry.FellLogs(ids, "stand-1", "mill-1", 2,
                EntityId.For(EntityKind.Person, 5), 221, diagnostics);
            Assert.NotNull(lot);
            Assert.AreEqual(2, lot.LogUnits);
        }

        [Test]
        public void FellViaRegistry_WithRights_ProducesLogLotWithStandProvenance()
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithGrantedStand("stand-1", "logging-1", 10, diagnostics);

            LogLot lot = registry.FellLogs(ids, "stand-1", "logging-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);

            Assert.NotNull(lot);
            Assert.AreEqual(4, lot.LogUnits);
            Assert.IsTrue(lot.LotId.IsValid);
            Assert.AreEqual("stand-1", lot.StandId, "Provenance travels with the lot.");
            Assert.AreEqual("white pine", lot.Species);
            Assert.AreEqual(6, registry.GetStand("stand-1").StandingTimberUnits);
        }

        [Test]
        public void FellViaRegistry_WithoutRights_RefusedLoudly_StandUntouched()
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithGrantedStand("stand-1", "logging-1", 10, diagnostics);

            LogLot lot = registry.FellLogs(ids, "stand-1", "poacher", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);

            Assert.IsNull(lot, "Timber theft is a claim, not a harvest.");
            Assert.AreEqual(10, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.IsTrue(diagnostics.Count > 0, "Refusals are loud, never silent.");
        }

        [Test]
        public void FellViaRegistry_UnknownStand_Refused()
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = new LoggingStandRegistry();

            LogLot lot = registry.FellLogs(ids, "stand-nope", "logging-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);

            Assert.IsNull(lot, "Logs must come from a registered timber stand.");
            Assert.IsTrue(diagnostics.Count > 0);
        }

        [Test]
        public void SawmillIntake_AcceptsProvenanceLot_RefusesAnonymousLot()
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithGrantedStand("stand-1", "logging-1", 10, diagnostics);
            var millStock = new SawmillLogStock();

            LogLot provenanced = registry.FellLogs(ids, "stand-1", "logging-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);
            Assert.IsNull(millStock.ReceiveLogLot(provenanced, diagnostics),
                "The W4A gate accepts logs that name their stand.");
            Assert.AreEqual(4, millStock.TotalLogUnits);

            var anonymous = new LogLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                LogUnits = 3,
                StandId = string.Empty,
            };
            Assert.NotNull(millStock.ReceiveLogLot(anonymous, diagnostics),
                "Anonymous logs are refused loudly — a lot with no stand is a claim, not inventory.");
            Assert.AreEqual(4, millStock.TotalLogUnits, "Refused lots touch nothing.");
        }

        [Test]
        public void TaskDefinitions_Register_ForestrySkillAndEquipmentGated()
        {
            var authority = new TaskAuthority();
            var skills = new SkillService();
            var diagnostics = new List<string>();

            LoggingTaskDefinitions.RegisterTaskDefinitions(authority, skills, diagnostics);

            Assert.NotNull(skills.GetSkill(TimberHarvest.ForestrySkillId),
                "Logging tasks reuse the T1E forestry skill (TTS-3 extension path).");

            TaskDefinition skid = authority.GetDefinition(LoggingTaskIds.SkidLogs);
            Assert.NotNull(skid);
            Assert.AreEqual(TimberHarvest.ForestrySkillId, skid.RequiredSkillId);
            Assert.IsTrue(skid.EquipmentClasses.Contains("asset:draft-skid-team"),
                "Skidding is draft-team work (Canon 4.1).");

            TaskDefinition buck = authority.GetDefinition(LoggingTaskIds.LimbBuckLogs);
            Assert.NotNull(buck);
            Assert.IsTrue(buck.EquipmentClasses.Contains(
                "asset:crosscut-saw|asset:buck-saw"));

            TaskDefinition load = authority.GetDefinition(LoggingTaskIds.LoadLogWagon);
            Assert.NotNull(load);
            Assert.IsTrue(load.EquipmentClasses.Contains("asset:cant-hook"));

            TaskDefinition haul = authority.GetDefinition(LoggingTaskIds.HaulLogsToMill);
            Assert.NotNull(haul);
            Assert.IsTrue(haul.EquipmentClasses.Contains("asset:log-wagon"));

            // "fell-timber" stays owned by T1E — W4C never registers it.
            Assert.IsNull(authority.GetDefinition(TimberHarvest.FellTimberTaskId),
                "W4C registration does not touch T1E's fell-timber task.");

            // Second registration refuses duplicates deterministically.
            var second = new List<string>();
            LoggingTaskDefinitions.RegisterTaskDefinitions(authority, skills, second);
            Assert.IsTrue(second.Exists(d => d.Contains("not registered")),
                "Duplicate task ids are rejected deterministically.");
        }

        [Test]
        public void Felling_EquipmentGateRefuses_WithoutUsableTool()
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithGrantedStand("stand-1", "logging-1", 10, diagnostics);
            var runtime = new LoggingOperationRuntime("logging-1", ids);

            // No suppliers: nothing usable (HarnessMakerTests pattern).
            var bareGate = new EquipmentTaskGate();
            LogLot refused = runtime.RunFelling(registry, "stand-1", "logging-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics, bareGate);

            Assert.IsNull(refused, "Felling without an axe or saw is not a real method (Canon 4.1).");
            Assert.AreEqual(10, registry.GetStand("stand-1").StandingTimberUnits);

            // With a usable felling axe owned by the business, felling proceeds.
            var toolGate = new EquipmentTaskGate
            {
                AssetsSupplier = () => new List<EquipmentAsset>
                {
                    new EquipmentAsset
                    {
                        AssetId = "axe-1",
                        Kind = "felling-axe",
                        OwnerKind = "business",
                        OwnerId = "logging-1",
                        Condition01 = 1f,
                    },
                },
            };
            LogLot felled = runtime.RunFelling(registry, "stand-1", "logging-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics, toolGate);

            Assert.NotNull(felled);
            Assert.AreEqual(4, felled.LogUnits);
            Assert.AreEqual("stand-1", felled.StandId);
            Assert.AreEqual(1, runtime.LandingStock.Count);
            Assert.AreEqual(4, runtime.LandingLogUnits);
        }

        [Test]
        public void Haul_DispatchUnroutable_RefusedLoudly_LotStaysAtLanding()
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithGrantedStand("stand-1", "logging-1", 10, diagnostics);
            var runtime = new LoggingOperationRuntime("logging-1", ids);
            LogLot lot = runtime.RunFelling(registry, "stand-1", "logging-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);
            Assert.NotNull(lot);

            // No edge connects the landing to the mill: the journey model
            // cannot route, so the haul is refused — travel is not assumed free.
            var stranded = new JourneyModel();
            Assert.IsNull(stranded.RegisterLocation(new JourneyLocation(
                "landing-1", JourneyLocationKind.Waypoint, "North Landing", 2f, 0f)));
            Assert.IsNull(stranded.RegisterLocation(new JourneyLocation(
                "mill-1-yard", JourneyLocationKind.Mill, "Local Sawmill", 0f, 0f)));

            LogHaul refused = runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                stranded, "landing-1", 221, diagnostics);

            Assert.IsNull(refused);
            Assert.AreEqual(1, runtime.LandingStock.Count, "The refused lot stays at the landing.");
            Assert.AreEqual(4, runtime.LandingLogUnits);
            Assert.IsTrue(diagnostics.Exists(d => d.Contains("no road connects")),
                "The refusal names the missing road loudly.");
        }

        [Test]
        public void Haul_DispatchWithoutWagon_RefusedLoudly()
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithGrantedStand("stand-1", "logging-1", 10, diagnostics);
            var runtime = new LoggingOperationRuntime("logging-1", ids);
            LogLot lot = runtime.RunFelling(registry, "stand-1", "logging-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);
            Assert.NotNull(lot);

            var bareGate = new EquipmentTaskGate(); // no wagon anywhere
            LogHaul refused = runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                LandingToMillJourney(), "landing-1", 221, diagnostics, bareGate);

            Assert.IsNull(refused, "No wagon, no haul (Canon 4.1).");
            Assert.AreEqual(1, runtime.LandingStock.Count);
        }

        [Test]
        public void Haul_FullJourney_DeliversLotToMillStock_WithProvenance()
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithGrantedStand("stand-1", "logging-1", 10, diagnostics);
            var runtime = new LoggingOperationRuntime("logging-1", ids);
            LogLot lot = runtime.RunFelling(registry, "stand-1", "logging-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);
            Assert.NotNull(lot);
            var millStock = new SawmillLogStock();

            LogHaul haul = runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                LandingToMillJourney(), "landing-1", 221, diagnostics);
            Assert.NotNull(haul);
            Assert.AreEqual(LogHaulStatus.Loading, haul.Status);
            Assert.AreEqual(0, runtime.LandingLogUnits,
                "The dispatched lot is held by the haul — not at the landing anymore.");
            Assert.IsNotNull(haul.Lot, "The haul physically holds the lot in transit.");

            // 2 miles at wagon speed (4 mph) = 30 min transit + 8 min loading.
            runtime.AdvanceHauls(1000, diagnostics);
            Assert.AreEqual(LogHaulStatus.Arrived, haul.Status);

            runtime.DeliverArrivedHauls(millStock, diagnostics);
            Assert.AreEqual(LogHaulStatus.Delivered, haul.Status);
            Assert.IsNull(haul.Lot, "Custody transferred — the mill owns the lot now.");
            Assert.AreEqual(4, millStock.TotalLogUnits);
            Assert.AreEqual("stand-1", millStock.Lots[0].StandId,
                "Provenance survives the journey: stand -> lot -> haul -> mill intake.");
            Assert.AreEqual(0, runtime.LandingLogUnits, "One physical lot is never in two places.");
        }

        [Test]
        public void Haul_HoldsLotPhysically_InTransit_NeverDuplicated()
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithGrantedStand("stand-1", "logging-1", 10, diagnostics);
            var runtime = new LoggingOperationRuntime("logging-1", ids);
            LogLot lot = runtime.RunFelling(registry, "stand-1", "logging-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);

            LogHaul haul = runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                LandingToMillJourney(), "landing-1", 221, diagnostics);
            Assert.NotNull(haul);

            // Mid-journey: exactly one holder of the physical lot.
            runtime.AdvanceHauls(10, diagnostics); // still loading/early transit
            Assert.IsTrue(haul.IsActive);
            int holders = 0;
            if (runtime.LandingLogUnits > 0) holders++;
            if (haul.Lot != null) holders++;
            Assert.AreEqual(1, holders, "The lot is in exactly one place while the haul is active.");
        }

        [Test]
        public void Regrowth_ExplicitRateOnly_NothingRegrowsOnItsOwn()
        {
            var diagnostics = new List<string>();
            var registry = new LoggingStandRegistry();
            Assert.IsNull(registry.RegisterStand(AuthoredStand("stand-1", 10)));

            // No regrowth call, no regrowth — never assumed.
            Assert.AreEqual(10, registry.GetStand("stand-1").StandingTimberUnits);

            // The caller supplies the rate explicitly (calibration is
            // Kennedy's open design question; no rate is guessed here).
            registry.ApplyRegrowth(3, 300, diagnostics);
            Assert.AreEqual(13, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.IsTrue(diagnostics.Exists(d => d.Contains("nothing regrows on its own")));
        }

        [Test]
        public void SaveLoad_RoundTrips_RegistryAndRuntime()
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = RegistryWithGrantedStand("stand-1", "logging-1", 10, diagnostics);
            var runtime = new LoggingOperationRuntime("logging-1", ids);
            LogLot lot = runtime.RunFelling(registry, "stand-1", "logging-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);
            LogHaul haul = runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                LandingToMillJourney(), "landing-1", 221, diagnostics);
            Assert.NotNull(haul);

            // Round-trip the registry.
            var restoredRegistry = new LoggingStandRegistry();
            restoredRegistry.LoadFromSaveDto(registry.CaptureSaveDto());
            Assert.AreEqual(1, restoredRegistry.StandCount);
            Assert.AreEqual(6, restoredRegistry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual("logging-1", restoredRegistry.CurrentRightsHolder("stand-1"));
            Assert.AreEqual(1, restoredRegistry.RightsGrants.Count);
            Assert.AreEqual("town-land-office", restoredRegistry.RightsGrants[0].GrantedBy);

            // Round-trip the runtime, including the in-transit haul.
            var restoredRuntime = new LoggingOperationRuntime("other", new EntityIdRegistry());
            restoredRuntime.LoadFromSaveDto(runtime.CaptureSaveDto());
            Assert.AreEqual("logging-1", restoredRuntime.BusinessInstanceId);
            Assert.AreEqual(0, restoredRuntime.LandingLogUnits);
            Assert.AreEqual(1, restoredRuntime.Hauls.Count);
            LogHaul restoredHaul = restoredRuntime.Hauls[0];
            Assert.AreEqual(LogHaulStatus.Loading, restoredHaul.Status);
            Assert.IsNotNull(restoredHaul.Lot);
            Assert.AreEqual("stand-1", restoredHaul.Lot.StandId);
            Assert.AreEqual(4, restoredHaul.Lot.LogUnits);
            Assert.IsTrue(restoredHaul.HaulId.IsValid);
        }
    }
}
