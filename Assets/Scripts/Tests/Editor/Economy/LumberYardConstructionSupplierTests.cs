using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Slice C3: the yard construction supplier adapter. The yard is
    /// seeded through its existing ReceiveLumberFromSawmill and
    /// ReceiveHardwareFromBlacksmith paths with real lots, following
    /// the C1 fixture pattern. The adapter reads the yard's real stock
    /// (sale-available lumber, hardware units), quotes through the C1
    /// price port, and refuses directory sales — the yard sells
    /// construction material only through the construction sale
    /// adapter. Registration replaces by business id (QA-13).
    /// </summary>
    [TestFixture]
    public sealed class LumberYardConstructionSupplierTests
    {
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
        }

        private static SawmillLumberLot DocumentedMillLot(int lotNumber, int units, string millId, string standId)
        {
            return new SawmillLumberLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LumberUnits = units,
                SourceLogLotId = "log-" + lotNumber,
                StandId = standId,
                Species = "white pine",
                MillBusinessId = millId,
                SawedBy = EntityId.For(EntityKind.Person, 5002),
                SawedDayIndex = 210,
                ConversionProfileId = "softwood-standard",
            };
        }

        private LumberYardShopRuntime StockedYard()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9401, 40, "mill-1", "stand-1"), "merchantable", 220, 85, diag),
                "test setup must stock merchantable lumber: " + string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9402, 30, "mill-2", "stand-2"), "clear", 230, 95, diag),
                "test setup must stock clear lumber: " + string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(LumberYardHardwareKind.Nails, 20, "smith-1", 218, "iron-order-7", 220, 30, diag),
                "test setup must stock nails: " + string.Join("; ", diag));
            return yard;
        }

        private LumberYardConstructionSupplier Adapter(LumberYardShopRuntime yard)
        {
            return new LumberYardConstructionSupplier(yard, new LumberYardConstructionPricePort(yard));
        }

        private static ConstructionMaterialRequirement LumberRequirement()
        {
            return new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-lumber",
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "lumber",
                RequiredUnits = 10,
                UnitLabel = "board feet",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
            };
        }

        private static List<IGoodsSupplier> SuppliersFor(ISupplierDirectory directory, string categoryId)
        {
            var found = new List<IGoodsSupplier>();
            foreach (IGoodsSupplier supplier in directory.SuppliersFor(categoryId))
                found.Add(supplier);
            return found;
        }

        [Test]
        public void HasCategory_Lumber_TrueWhenSaleAvailable()
        {
            var yard = StockedYard();
            var adapter = Adapter(yard);

            Assert.IsTrue(adapter.HasCategory("construction-lumber"),
                "a stocked yard offers construction lumber");
        }

        [Test]
        public void HasCategory_Hardware_TrueWhenSaleAvailable()
        {
            var yard = StockedYard();
            var adapter = Adapter(yard);

            Assert.IsTrue(adapter.HasCategory("construction-hardware"),
                "a stocked yard offers construction hardware");
        }

        [Test]
        public void HasCategory_BothFalseWhenEmpty()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var adapter = Adapter(yard);

            Assert.IsFalse(adapter.HasCategory("construction-lumber"),
                "an empty yard offers no lumber");
            Assert.IsFalse(adapter.HasCategory("construction-hardware"),
                "an empty yard offers no hardware");
        }

        [Test]
        public void HasCategory_Lumber_FalseWhenAllEarmarked()
        {
            var yard = StockedYard();
            var adapter = Adapter(yard);

            Assert.IsNull(yard.EarmarkLumberForProject(yard.LumberStock.Lots[0].LotId, "proj-1", 40, 220, diag),
                "test setup must earmark lot 9401: " + string.Join("; ", diag));
            Assert.IsNull(yard.EarmarkLumberForProject(yard.LumberStock.Lots[1].LotId, "proj-1", 30, 220, diag),
                "test setup must earmark lot 9402: " + string.Join("; ", diag));

            Assert.IsFalse(adapter.HasCategory("construction-lumber"),
                "earmarked lumber is not sale-available (Canon 6.4)");
            Assert.IsTrue(adapter.HasCategory("construction-hardware"),
                "earmarking lumber leaves hardware offered");
        }

        [Test]
        public void HasCategory_OtherCategories_False()
        {
            var yard = StockedYard();
            var adapter = Adapter(yard);

            Assert.IsFalse(adapter.HasCategory("construction-masonry"),
                "the yard offers no masonry");
            Assert.IsFalse(adapter.HasCategory("construction-materials"),
                "the yard offers no generic construction materials");
            Assert.IsFalse(adapter.HasCategory(string.Empty),
                "an empty category is never offered");
        }

        [Test]
        public void StockUnits_Lumber_EqualsSaleAvailable()
        {
            var yard = StockedYard();
            var adapter = Adapter(yard);

            Assert.AreEqual(70, adapter.StockUnits("construction-lumber"),
                "two stocked lots, 40 + 30 units");
            Assert.IsNull(yard.EarmarkLumberForProject(yard.LumberStock.Lots[0].LotId, "proj-1", 40, 220, diag),
                "test setup must earmark lot 9401: " + string.Join("; ", diag));
            Assert.AreEqual(30, adapter.StockUnits("construction-lumber"),
                "earmarking 40 leaves 30 sale-available");
        }

        [Test]
        public void StockUnits_Hardware_EqualsUnits()
        {
            var yard = StockedYard();
            var adapter = Adapter(yard);

            Assert.AreEqual(20, adapter.StockUnits("construction-hardware"),
                "20 nails stocked");
            var empty = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            Assert.AreEqual(0, Adapter(empty).StockUnits("construction-hardware"),
                "an empty yard holds no hardware");
        }

        [Test]
        public void PricePerUnitCents_EqualsPortQuote()
        {
            var yard = StockedYard();
            var adapter = Adapter(yard);

            Assert.AreEqual(112, adapter.PricePerUnitCents("construction-lumber"),
                "lumber quotes the C1 port price, 112c");
            Assert.AreEqual(30, adapter.PricePerUnitCents("construction-hardware"),
                "hardware quotes the C1 port price, 30c");
            var empty = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var emptyAdapter = Adapter(empty);
            Assert.AreEqual(0, emptyAdapter.PricePerUnitCents("construction-lumber"),
                "the port refuses an empty yard with -1, clamped to 0");
            Assert.AreEqual(0, emptyAdapter.PricePerUnitCents("construction-hardware"),
                "the port refuses an empty yard with -1, clamped to 0");
        }

        [Test]
        public void PricePerUnitCents_NullPort_ReturnsZero()
        {
            var yard = StockedYard();
            var adapter = new LumberYardConstructionSupplier(yard, null);

            Assert.AreEqual(0, adapter.PricePerUnitCents("construction-lumber"),
                "a null port prices nothing");
            Assert.AreEqual(0, adapter.PricePerUnitCents("construction-hardware"),
                "a null port prices nothing");
        }

        [Test]
        public void Sell_RefusesZero()
        {
            var yard = StockedYard();
            var adapter = Adapter(yard);

            Assert.AreEqual(0, adapter.Sell("construction-lumber", 5, 220, diag),
                "directory sales are refused — the yard sells only through the sale adapter");
            Assert.IsTrue(string.Join("; ", diag).Contains("sale adapter"),
                "the refusal names the sale adapter: " + string.Join("; ", diag));
            Assert.AreEqual(70, adapter.StockUnits("construction-lumber"),
                "a refused sale moves no stock");
        }

        [Test]
        public void RegisterIn_BothCategoriesDiscoverable()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);
            var directory = new SupplierDirectory();

            LumberYardConstructionSupplier.RegisterIn(directory, yard, port);

            List<IGoodsSupplier> lumber = SuppliersFor(directory, "construction-lumber");
            Assert.AreEqual(1, lumber.Count, "the yard is discoverable for lumber");
            Assert.AreEqual("yard-1", lumber[0].SupplierBusinessId);
            List<IGoodsSupplier> hardware = SuppliersFor(directory, "construction-hardware");
            Assert.AreEqual(1, hardware.Count, "the yard is discoverable for hardware");
            Assert.AreEqual("yard-1", hardware[0].SupplierBusinessId);
            Assert.AreEqual(0, SuppliersFor(directory, "construction-masonry").Count,
                "the yard is not discoverable for masonry");
        }

        [Test]
        public void RegisterIn_ReRegister_ReplacesByBusinessId()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);
            var directory = new SupplierDirectory();
            var first = new LumberYardConstructionSupplier(yard, port);
            var second = new LumberYardConstructionSupplier(StockedYard(), port);

            directory.Register(first);
            directory.Register(second);

            List<IGoodsSupplier> lumber = SuppliersFor(directory, "construction-lumber");
            Assert.AreEqual(1, lumber.Count, "re-registering the same business id replaces, never duplicates");
            Assert.IsTrue(object.ReferenceEquals(second, lumber[0]),
                "the second registration is the live one");
        }

        [Test]
        public void DiscoverSupplier_Unnamed_DiscoversYard()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);
            var directory = new SupplierDirectory();
            LumberYardConstructionSupplier.RegisterIn(directory, yard, port);
            var service = new ConstructionProcurementService();
            var requirement = LumberRequirement();

            string supplierId = service.DiscoverSupplier(requirement, directory, diag);

            Assert.AreEqual("yard-1", supplierId, "the unnamed requirement discovers the yard");
            Assert.AreEqual("yard-1", requirement.SupplierBusinessId,
                "discovery writes the supplier onto the requirement");
        }

        [Test]
        public void DiscoverSupplier_NamedYard_Verified()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);
            var directory = new SupplierDirectory();
            LumberYardConstructionSupplier.RegisterIn(directory, yard, port);
            var service = new ConstructionProcurementService();
            var requirement = LumberRequirement();
            requirement.SupplierBusinessId = "yard-1";

            string supplierId = service.DiscoverSupplier(requirement, directory, diag);

            Assert.AreEqual("yard-1", supplierId, "the named yard verifies as a real, discoverable supplier");
        }

        [Test]
        public void DiscoverSupplier_Unregistered_Refuses()
        {
            var directory = new SupplierDirectory();
            var service = new ConstructionProcurementService();

            string supplierId = service.DiscoverSupplier(LumberRequirement(), directory, diag);

            Assert.IsNull(supplierId, "an empty directory discovers nothing — the registration is what makes discovery work");
        }
    }
}
