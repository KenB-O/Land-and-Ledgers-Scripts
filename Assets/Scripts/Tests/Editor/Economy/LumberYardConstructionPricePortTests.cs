using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Slice C1: the yard backed IConstructionMaterialPricePort adapter.
    /// The yard is seeded through its existing ReceiveLumberFromSawmill
    /// and ReceiveHardwareFromBlacksmith paths with real lots, following
    /// the LumberYardConstructionMaterialSourceTests fixture pattern.
    /// Lumber prices equal the yard quote, hardware prices equal the
    /// first stocked lot cost, and every other kind refuses with minus
    /// one. The port never invents a price and never moves money. It
    /// ships with no production binding; the hub ownership landed in
    /// C2 and the binding is the C3 slice.
    /// </summary>
    [TestFixture]
    public sealed class LumberYardConstructionPricePortTests
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

        private static ConstructionMaterialRequirement LumberRequirement()
        {
            return new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-lumber",
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "lumber",
                RequiredUnits = 10,
                UnitLabel = "board feet",
            };
        }

        private static ConstructionMaterialRequirement HardwareRequirement()
        {
            return new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-nails",
                MaterialKind = ConstructionMaterialKind.Hardware,
                MaterialDisplayName = "nails",
                RequiredUnits = 5,
                UnitLabel = "pounds",
            };
        }

        [Test]
        public void Lumber_BlankGrade_QuotesYardAverage()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);

            Assert.AreEqual(112, port.UnitPriceCents(LumberRequirement()),
                "the lumber price is the yard average 89.3c at 25 percent markup, 112c");
            Assert.AreEqual(112, port.UnitPriceCents(LumberRequirement()),
                "the quote must be deterministic on the unchanged yard, which the adapter's one-consistent-price rule needs");
            Assert.AreEqual(0, yard.SalesHistory.Count, "pricing must record no sale");
        }

        [Test]
        public void Lumber_MerchantableGrade_Ignored_QuotesYardAverage()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);
            var requirement = LumberRequirement();
            requirement.GradeId = "merchantable";

            Assert.AreEqual(112, port.UnitPriceCents(requirement),
                "a named grade is ignored: the port quotes the yard average, matching the grade-blind sale draw");
        }

        [Test]
        public void Lumber_ClearGrade_Ignored_QuotesYardAverage()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);
            var requirement = LumberRequirement();
            requirement.GradeId = "clear";

            Assert.AreEqual(112, port.UnitPriceCents(requirement),
                "a named grade is ignored: the port quotes the yard average, matching the grade-blind sale draw");
        }

        [Test]
        public void Lumber_UnknownGrade_Ignored_QuotesYardAverage()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);
            var requirement = LumberRequirement();
            requirement.GradeId = "bogus";

            Assert.AreEqual(112, port.UnitPriceCents(requirement),
                "an unknown grade is ignored like any other grade: the port never filters by grade");
        }

        [Test]
        public void Lumber_EmptyYard_ReturnsMinusOne()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var port = new LumberYardConstructionPricePort(yard);

            Assert.AreEqual(-1, port.UnitPriceCents(LumberRequirement()),
                "a quote with no matching stock is refused, never priced from thin air");
        }

        [Test]
        public void Hardware_Nails_EqualsFirstStockedLotCost()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);

            Assert.AreEqual(30, port.UnitPriceCents(HardwareRequirement()),
                "hardware is priced at the first stocked lot cost, 30c, with no markup");
            Assert.AreEqual(0, yard.SalesHistory.Count, "pricing must record no sale");
        }

        [Test]
        public void Hardware_EmptyYard_ReturnsMinusOne()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var port = new LumberYardConstructionPricePort(yard);

            Assert.AreEqual(-1, port.UnitPriceCents(HardwareRequirement()),
                "hardware with no stocked lot has no price and must refuse with minus one");
        }

        [Test]
        public void Hardware_ZeroCostLot_ReturnsMinusOne()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(LumberYardHardwareKind.Nails, 20, "smith-1", 218, "iron-order-7", 220, 0, diag),
                "test setup must stock zero cost nails: " + string.Join("; ", diag));
            var port = new LumberYardConstructionPricePort(yard);

            Assert.AreEqual(-1, port.UnitPriceCents(HardwareRequirement()),
                "a lot with no readable unit cost must refuse rather than price from nothing");
        }

        [Test]
        public void NonStockedKind_ReturnsMinusOne()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);

            foreach (ConstructionMaterialKind kind in new[]
            {
                ConstructionMaterialKind.Masonry,
                ConstructionMaterialKind.Unspecified,
                ConstructionMaterialKind.Paint,
                ConstructionMaterialKind.Glass,
                ConstructionMaterialKind.Roofing,
                ConstructionMaterialKind.Other,
            })
            {
                var requirement = new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-other",
                    MaterialKind = kind,
                    MaterialDisplayName = kind.ToString().ToLowerInvariant(),
                    RequiredUnits = 5,
                };
                Assert.AreEqual(-1, port.UnitPriceCents(requirement),
                    "the yard holds no '" + kind + "' price for a build");
            }
        }

        [Test]
        public void Guards_NullYardNullRequirementNonPositiveUnits_ReturnMinusOne()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);
            var unbound = new LumberYardConstructionPricePort(null);

            Assert.AreEqual(-1, unbound.UnitPriceCents(LumberRequirement()),
                "a port with no yard runtime must refuse");
            Assert.AreEqual(-1, port.UnitPriceCents(null),
                "a null requirement must refuse");
            var zeroUnits = LumberRequirement();
            zeroUnits.RequiredUnits = 0;
            Assert.AreEqual(-1, port.UnitPriceCents(zeroUnits),
                "zero units must refuse");
            var negativeUnits = LumberRequirement();
            negativeUnits.RequiredUnits = -5;
            Assert.AreEqual(-1, port.UnitPriceCents(negativeUnits),
                "negative units must refuse");
            Assert.AreEqual(0, yard.SalesHistory.Count, "every refused call records nothing");
        }
    }
}
