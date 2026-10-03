using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Freight;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// EQU-3: draft power — the horse + implement + harness + driver work unit.
    /// Plowing requires the full unit (Canon: plow, draft power and harness);
    /// horses are bought, never free; one reservation authority (Tech X §3.8)
    /// serves both the plow unit and the freight pool.
    /// </summary>
    [TestFixture]
    public sealed class DraftPowerTests
    {
        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private sealed class Fixture
        {
            public AnimalRegistry Registry;
            public List<EntityId> Horses = new List<EntityId>();
            public List<EntityId> Drivers = new List<EntityId>();
            public EquipmentAsset Plow;
            public EquipmentAsset Harness;
        }

        private static Fixture NewFixture(string owner = "ranch-1")
        {
            var f = new Fixture();
            f.Registry = new AnimalRegistry(new EntityIdRegistry());
            for (int i = 0; i < 3; i++)
            {
                AnimalState horse = f.Registry.RegisterAnimal(AnimalSpecies.Horse, "Draft",
                    AnimalSex.Female, AnimalOwnerKind.Business, owner, 0, "test herd");
                f.Horses.Add(horse.AnimalId);
            }
            f.Drivers.Add(Person(11));
            f.Drivers.Add(Person(12));
            f.Plow = new EquipmentAsset
            {
                AssetId = "EQ-test-001", Kind = "plow", DisplayName = "Moldboard plow",
                Condition01 = 1f, OwnerKind = "business", OwnerId = "farm-1",
                MadeByBusinessId = "smith-1", MadeDayIndex = 5,
            };
            f.Harness = new EquipmentAsset
            {
                AssetId = "EQ-test-002", Kind = "harness", DisplayName = "Team harness",
                Condition01 = 1f, OwnerKind = "business", OwnerId = "farm-1",
                MadeByBusinessName = "purchased from general store",
            };
            return f;
        }

        [Test]
        public void PlowUnit_Assembles_WithFullTeam()
        {
            var f = NewFixture();
            var power = new DraftPowerService();
            var diagnostics = new List<string>();

            DraftWorkUnit unit = DraftWorkUnitService.AssemblePlowUnit(
                power, f.Registry, f.Horses, f.Drivers, f.Plow, f.Harness,
                "plowing north-40", diagnostics);

            Assert.IsNotNull(unit, string.Join("; ", diagnostics));
            Assert.AreEqual(2, unit.TeamAnimalIds.Count);
            Assert.AreEqual(f.Drivers[0], unit.DriverPersonId);
            Assert.IsTrue(f.Plow.IsReserved, "implement reserved through the unit");
        }

        [Test]
        public void PlowUnit_Refused_WithoutFullTeam()
        {
            var f = NewFixture();
            var power = new DraftPowerService();
            var diagnostics = new List<string>();

            // Only one horse offered — no team, no plowing.
            var oneHorse = new List<EntityId> { f.Horses[0] };
            DraftWorkUnit unit = DraftWorkUnitService.AssemblePlowUnit(
                power, f.Registry, oneHorse, f.Drivers, f.Plow, f.Harness,
                "plowing", diagnostics);

            Assert.IsNull(unit);
        }

        [Test]
        public void PlowUnit_Refused_WithWreckedPlow()
        {
            var f = NewFixture();
            f.Plow.ApplyWear(1f); // wrecked
            var power = new DraftPowerService();

            DraftWorkUnit unit = DraftWorkUnitService.AssemblePlowUnit(
                power, f.Registry, f.Horses, f.Drivers, f.Plow, f.Harness,
                "plowing", new List<string>());

            Assert.IsNull(unit, "wrecked plow must be repaired first (Tech X §3.9)");
        }

        [Test]
        public void OneAuthority_NoDoubleBooking_AcrossPoolAndPlow()
        {
            var f = NewFixture();

            // The freight pool reserves through the shared authority now.
            var pool = new FreightResourcePool();
            pool.AddWagon(new FreightWagon("W-1", "Test Wagon", 600));
            var diagnostics = new List<string>();
            Assert.IsTrue(pool.TryReserveTeam(100, f.Horses, f.Drivers, "freight run",
                out DraftTeamReservation reservation, diagnostics),
                string.Join("; ", diagnostics));

            // Those two horses are reserved through the pool's own authority —
            // a plow unit addressing the SAME authority cannot take them.
            // (Only one horse remains free, and the unit needs two.)
            DraftWorkUnit unit = DraftWorkUnitService.AssemblePlowUnit(
                pool.DraftPower, f.Registry, f.Horses, f.Drivers, f.Plow, f.Harness,
                "plowing", new List<string>());
            Assert.IsNull(unit, "horses hauling freight cannot simultaneously plow");

            pool.ReleaseTeam(reservation);
            DraftWorkUnit unit2 = DraftWorkUnitService.AssemblePlowUnit(
                pool.DraftPower, f.Registry, f.Horses, f.Drivers, f.Plow, f.Harness,
                "plowing", new List<string>());
            Assert.IsNotNull(unit2, "released horses plow again");

            DraftWorkUnitService.ReleaseUnit(unit2, pool.DraftPower);
        }

        [Test]
        public void HorseTrade_TransfersOwnership_WithProvenance()
        {
            var f = NewFixture("ranch-1");
            var diagnostics = new List<string>();
            var sellerLedger = new HouseholdLedger(21);
            sellerLedger.RecordInflow(0, 5000, HouseholdIncomeSource.OtherDocumented,
                "test", "test funds", "test");

            HorseSale sale = HorseTrade.ExecuteSale(f.Registry, f.Horses[0],
                "ranch-1", "Test Ranch", "farm-1", "Test Farm", 4500, 30,
                sellerLedger, diagnostics);

            Assert.IsNotNull(sale, string.Join("; ", diagnostics));
            AnimalState horse = f.Registry.GetAnimal(f.Horses[0]);
            Assert.AreEqual("farm-1", horse.OwnerId);
            Assert.AreEqual(2, horse.OwnershipHistory.Count, "history appended, never rewritten");
            Assert.AreEqual(5000 + 4500, sellerLedger.GetBalanceCents());
        }

        [Test]
        public void HorseTrade_Refuses_NonHorse()
        {
            var f = new Fixture();
            f.Registry = new AnimalRegistry(new EntityIdRegistry());
            AnimalState cow = f.Registry.RegisterAnimal(AnimalSpecies.Cattle, "Shorthorn",
                AnimalSex.Female, AnimalOwnerKind.Business, "ranch-1", 0, "test");
            var diagnostics = new List<string>();

            HorseSale sale = HorseTrade.ExecuteSale(f.Registry, cow.AnimalId,
                "ranch-1", "Test Ranch", "farm-1", "Test Farm", 1000, 30,
                new HouseholdLedger(22), diagnostics);

            Assert.IsNull(sale, "cattle are not horses");
        }

        [Test]
        public void CompletePlowing_RequiresUnit_AndWearsPlow()
        {
            var chain = new CropChain(new CropFieldAuthority());
            var field = new CropFieldState { FieldId = "north-40", FarmId = "farm-1", Acres = 40 };
            chain.Fields.RegisterField(field);
            var diagnostics = new List<string>();

            // No unit: refused.
            string refused = chain.CompletePlowing("north-40", null, 100, diagnostics);
            Assert.IsNotNull(refused);
            Assert.AreEqual(CropGrowthState.Fallow, chain.Fields.GetField("north-40").GrowthState);

            // With unit: transitions + wears the plow.
            var f = NewFixture();
            var power = new DraftPowerService();
            DraftWorkUnit unit = DraftWorkUnitService.AssemblePlowUnit(
                power, f.Registry, f.Horses, f.Drivers, f.Plow, f.Harness,
                "plowing north-40", diagnostics);
            Assert.IsNotNull(unit);

            string ok = chain.CompletePlowing("north-40", unit, 100, diagnostics);
            Assert.IsNull(ok, ok);
            Assert.AreEqual(CropGrowthState.Prepared, chain.Fields.GetField("north-40").GrowthState);
            Assert.Less(f.Plow.Condition01, 1f, "plowing wears the share (Tech X §3.9)");
        }

        [Test]
        public void WorkingHorses_EatMore()
        {
            var loop = new FeedLoop(1000);
            var diagnostics = new List<string>();
            int shortfallIdle = loop.ConsumeDay(0, 0, 0, 0, 2, 0, FarmSeason.Spring,
                null, null, null, null, diagnostics);
            int afterIdle = loop.FeedStockUnits;

            var loop2 = new FeedLoop(1000);
            int shortfallWorking = loop2.ConsumeDay(0, 0, 0, 0, 0, 2, FarmSeason.Spring,
                null, null, null, null, diagnostics);

            Assert.AreEqual(0, shortfallIdle);
            Assert.AreEqual(0, shortfallWorking);
            Assert.Less(loop2.FeedStockUnits, afterIdle, "working horses eat more — honest upkeep");
        }
    }
}
