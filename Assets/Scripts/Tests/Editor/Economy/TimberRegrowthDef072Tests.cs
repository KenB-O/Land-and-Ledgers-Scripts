using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// DEF-072 (Decision 15): timber regrowth — gradual, species- and
    /// site-dependent, never instant mature timber. A harvested stand
    /// regrows young cohorts that become merchantable only after the
    /// species maturity years (white pine 80 on a productive site);
    /// the seed source is the mature stock left standing, so a
    /// clear-cut stand establishes nothing; a full stand never grows
    /// past its carrying capacity. Day arithmetic: year = 365 days.
    /// </summary>
    [TestFixture]
    public sealed class TimberRegrowthDef072Tests
    {
        private const int DaysPerYear = 365;
        private const float Tolerance = 0.001f;

        private static TimberStand PineStand(string standId, int capacity, int standing, float siteQuality01 = 1f)
        {
            return new TimberStand
            {
                StandId = standId,
                DisplayName = "Test Pines",
                LocationId = "landing-1",
                TimberRightsHolderId = "logging-1",
                Species = "eastern white pine",
                KilogramsPerLog = 600f,
                StandingTimberUnits = standing,
                CarryingCapacityUnits = capacity,
                SiteQuality01 = siteQuality01,
            };
        }

        private static TimberStand OakStand(string standId, int capacity, int standing)
        {
            TimberStand stand = PineStand(standId, capacity, standing);
            stand.Species = "oak";
            return stand;
        }

        private static LoggingStandRegistry RegistryWithStand(TimberStand stand)
        {
            var registry = new LoggingStandRegistry();
            Assert.IsNull(registry.RegisterStand(stand));
            return registry;
        }

        private static LogLot Fell(TimberStand stand, int units, int dayIndex)
        {
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            return TimberHarvest.FellLogs(ids, stand, "logging-1", units,
                EntityId.For(EntityKind.Person, 3), dayIndex, diagnostics);
        }

        [Test]
        public void T1_NoDrive_NoRegrowth()
        {
            TimberStand stand = PineStand("stand-1", 80, 80);
            Assert.NotNull(Fell(stand, 40, 0));
            Assert.AreEqual(40, stand.StandingTimberUnits);

            // No drive, no regrowth — never assumed.
            Assert.AreEqual(40, stand.StandingTimberUnits);
            Assert.AreEqual(0f, stand.YoungGrowthUnits, Tolerance);
        }

        [Test]
        public void T2_DriveAfterPartialHarvest_YoungGrowthOnly()
        {
            // Capacity 80 pine, fell 40 -> seed 0.5 -> establishment
            // (80/80) x 0.5 x 1.0 = 0.5 young/year. Nothing matures
            // before year 81, so the mature count does not move.
            TimberStand stand = PineStand("stand-1", 80, 80);
            var registry = RegistryWithStand(stand);
            Assert.NotNull(Fell(stand, 40, 0));
            var diagnostics = new List<string>();

            registry.DriveRegrowth(DaysPerYear, diagnostics);
            Assert.AreEqual(40, stand.StandingTimberUnits);
            Assert.AreEqual(0.5f, stand.YoungGrowthUnits, Tolerance);

            registry.DriveRegrowth(8 * DaysPerYear, diagnostics);
            Assert.AreEqual(40, stand.StandingTimberUnits);
            Assert.AreEqual(4.0f, stand.YoungGrowthUnits, Tolerance);
        }

        [Test]
        public void T3_FullRotation_ReturnsToCapacity()
        {
            TimberStand stand = PineStand("stand-1", 80, 80);
            var registry = RegistryWithStand(stand);
            Assert.NotNull(Fell(stand, 40, 0));
            var diagnostics = new List<string>();

            registry.DriveRegrowth(80 * DaysPerYear, diagnostics);
            Assert.AreEqual(40, stand.StandingTimberUnits);
            Assert.AreEqual(40.0f, stand.YoungGrowthUnits, Tolerance);

            // By year 160 every year-1..80 cohort (40.0 units) has
            // matured: 40 + 40 = 80. No establishment after year 80 —
            // the stand stays fully stocked (mature + young + accrual
            // = capacity), so the canopy admits no new seedlings.
            registry.DriveRegrowth(160 * DaysPerYear, diagnostics);
            Assert.AreEqual(80, stand.StandingTimberUnits);
            Assert.AreEqual(0f, stand.YoungGrowthUnits, Tolerance);
        }

        [Test]
        public void T4_ClearCut_NoSeedSource_NoRegrowth()
        {
            TimberStand stand = PineStand("stand-1", 80, 80);
            var registry = RegistryWithStand(stand);
            Assert.NotNull(Fell(stand, 80, 0));
            Assert.AreEqual(0, stand.StandingTimberUnits);
            var diagnostics = new List<string>();

            registry.DriveRegrowth(80 * DaysPerYear, diagnostics);
            Assert.AreEqual(0, stand.StandingTimberUnits);
            Assert.AreEqual(0f, stand.YoungGrowthUnits, Tolerance);
        }

        [Test]
        public void T5_FullStand_NoGrowthBeyondCapacity()
        {
            TimberStand stand = PineStand("stand-1", 80, 80);
            var registry = RegistryWithStand(stand);
            var diagnostics = new List<string>();

            registry.DriveRegrowth(80 * DaysPerYear, diagnostics);
            Assert.AreEqual(80, stand.StandingTimberUnits);
            Assert.AreEqual(0f, stand.YoungGrowthUnits, Tolerance);
        }

        [Test]
        public void T6_SiteQuality_ScalesEstablishment()
        {
            TimberStand rich = PineStand("stand-rich", 80, 80, 1f);
            TimberStand poor = PineStand("stand-poor", 80, 80, 0.5f);
            var registry = new LoggingStandRegistry();
            Assert.IsNull(registry.RegisterStand(rich));
            Assert.IsNull(registry.RegisterStand(poor));
            Assert.NotNull(Fell(rich, 40, 0));
            Assert.NotNull(Fell(poor, 40, 0));
            var diagnostics = new List<string>();

            registry.DriveRegrowth(DaysPerYear, diagnostics);
            Assert.AreEqual(0.5f, rich.YoungGrowthUnits, Tolerance);
            Assert.AreEqual(0.25f, poor.YoungGrowthUnits, Tolerance);
        }

        [Test]
        public void T7_SpeciesMaturity_OakSlowerThanPine()
        {
            // The maturity table itself (white pine 80 is the decided
            // baseline; oak 100, ash 70, birch 60 are provisional;
            // unknown species fall back to 80).
            Assert.AreEqual(80, TimberSpecies.MaturityYears("eastern white pine"));
            Assert.AreEqual(80, TimberSpecies.MaturityYears("white pine"));
            Assert.AreEqual(100, TimberSpecies.MaturityYears("oak"));
            Assert.AreEqual(70, TimberSpecies.MaturityYears("ash"));
            Assert.AreEqual(60, TimberSpecies.MaturityYears("birch"));
            Assert.AreEqual(80, TimberSpecies.MaturityYears("maple"));
            Assert.AreEqual(80, TimberSpecies.MaturityYears(null));

            TimberStand pine = PineStand("stand-pine", 80, 80);
            TimberStand oak = OakStand("stand-oak", 80, 80);
            var registry = new LoggingStandRegistry();
            Assert.IsNull(registry.RegisterStand(pine));
            Assert.IsNull(registry.RegisterStand(oak));
            Assert.NotNull(Fell(pine, 40, 0));
            Assert.NotNull(Fell(oak, 40, 0));
            var diagnostics = new List<string>();

            // Pine establishes 0.5/year, oak (80/100) x 0.5 = 0.4/year.
            registry.DriveRegrowth(80 * DaysPerYear, diagnostics);
            Assert.AreEqual(40, pine.StandingTimberUnits);
            Assert.AreEqual(40.0f, pine.YoungGrowthUnits, Tolerance);
            Assert.AreEqual(40, oak.StandingTimberUnits);
            Assert.AreEqual(32.0f, oak.YoungGrowthUnits, Tolerance);

            // By year 100 the pine's year-1..20 cohorts (10.0 units)
            // have matured; no oak cohort matures before year 101.
            registry.DriveRegrowth(100 * DaysPerYear, diagnostics);
            Assert.AreEqual(50, pine.StandingTimberUnits);
            Assert.AreEqual(40, oak.StandingTimberUnits);
        }

        [Test]
        public void T8_SaveLoad_RoundTripsCohorts()
        {
            TimberStand stand = PineStand("stand-1", 80, 80);
            var registry = RegistryWithStand(stand);
            Assert.NotNull(Fell(stand, 40, 0));
            var diagnostics = new List<string>();
            registry.DriveRegrowth(8 * DaysPerYear, diagnostics);
            Assert.AreEqual(4.0f, stand.YoungGrowthUnits, Tolerance);

            var restoredRegistry = new LoggingStandRegistry();
            restoredRegistry.LoadFromSaveDto(registry.CaptureSaveDto());
            TimberStand restored = restoredRegistry.GetStand("stand-1");
            Assert.NotNull(restored);
            Assert.AreEqual(40, restored.StandingTimberUnits);
            Assert.AreEqual(4.0f, restored.YoungGrowthUnits, Tolerance);
            Assert.AreEqual(80, restored.CarryingCapacityUnits);
            Assert.AreEqual(1f, restored.SiteQuality01, Tolerance);
            Assert.AreEqual(8 * DaysPerYear, restored.LastRegrowthDayIndex);

            // The restored registry continues correctly with no
            // double-count of the first 8 years, and driving it does
            // not touch the original (the save copy is a deep copy).
            restoredRegistry.DriveRegrowth(160 * DaysPerYear, diagnostics);
            Assert.AreEqual(80, restored.StandingTimberUnits);
            Assert.AreEqual(0f, restored.YoungGrowthUnits, Tolerance);
            Assert.AreEqual(4.0f, stand.YoungGrowthUnits, Tolerance);
        }

        [Test]
        public void T9_OldSave_LoadsWithDefaults()
        {
            // A pre-DEF-072 save carries none of the regrowth fields.
            var dto = new LoggingStandRegistry.LoggingStandRegistrySaveDto
            {
                Stands = new List<TimberStand>
                {
                    new TimberStand
                    {
                        StandId = "old-stand",
                        DisplayName = "Old Pines",
                        Species = "white pine",
                        TimberRightsHolderId = "logging-1",
                        StandingTimberUnits = 40,
                    },
                },
            };
            var registry = new LoggingStandRegistry();
            registry.LoadFromSaveDto(dto);
            TimberStand stand = registry.GetStand("old-stand");
            Assert.NotNull(stand);
            Assert.AreEqual(40, stand.CarryingCapacityUnits);
            Assert.AreEqual(1f, stand.SiteQuality01, Tolerance);
            Assert.NotNull(stand.YoungCohorts);
            Assert.AreEqual(0, stand.YoungCohorts.Count);

            // The normalized stand drives: fell 20 (seed 0.5), one year
            // establishes (40/80) x 0.5 x 1.0 = 0.25 young.
            Assert.NotNull(Fell(stand, 20, 0));
            var diagnostics = new List<string>();
            registry.DriveRegrowth(DaysPerYear, diagnostics);
            Assert.AreEqual(20, stand.StandingTimberUnits);
            Assert.AreEqual(0.25f, stand.YoungGrowthUnits, Tolerance);
        }

        [Test]
        public void T10_YoungGrowth_NotFellable()
        {
            TimberStand stand = PineStand("stand-1", 80, 80);
            var registry = RegistryWithStand(stand);
            Assert.NotNull(Fell(stand, 40, 0));
            var diagnostics = new List<string>();
            registry.DriveRegrowth(8 * DaysPerYear, diagnostics);
            Assert.AreEqual(4.0f, stand.YoungGrowthUnits, Tolerance);

            LogLot lot = Fell(stand, 40, 8 * DaysPerYear);
            Assert.NotNull(lot);
            Assert.AreEqual(40, lot.LogUnits);
            Assert.AreEqual(0, stand.StandingTimberUnits);
            Assert.AreEqual(4.0f, stand.YoungGrowthUnits, Tolerance);
        }

        [Test]
        public void T11_SameDayReDrive_Idempotent()
        {
            TimberStand stand = PineStand("stand-1", 80, 80);
            var registry = RegistryWithStand(stand);
            Assert.NotNull(Fell(stand, 40, 0));
            var diagnostics = new List<string>();

            registry.DriveRegrowth(DaysPerYear, diagnostics);
            Assert.AreEqual(40, stand.StandingTimberUnits);
            Assert.AreEqual(0.5f, stand.YoungGrowthUnits, Tolerance);

            registry.DriveRegrowth(DaysPerYear, diagnostics);
            Assert.AreEqual(40, stand.StandingTimberUnits);
            Assert.AreEqual(0.5f, stand.YoungGrowthUnits, Tolerance);
        }
    }
}
