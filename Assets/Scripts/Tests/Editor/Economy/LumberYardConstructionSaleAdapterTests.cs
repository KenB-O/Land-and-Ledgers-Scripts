using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class LumberYardConstructionSaleAdapterTests
    {
        private sealed class MaterialPrices : IConstructionMaterialPricePort
        {
            public int UnitPriceCents(ConstructionMaterialRequirement requirement) => 12;
        }

        [Test]
        public void ConstructionProcurement_SellsOnlyFiniteNamedYardLotsAtQuotedPrice()
        {
            var ids = new EntityIdRegistry();
            var yard = new LumberYardShopRuntime("yard-1", ids);
            var diagnostics = new List<string>();
            Assert.IsNull(yard.ReceiveLumberPurchase(8, "white pine",
                LumberYardGradeCatalog.MerchantableGradeId, "sawmill-1", "mill sale 14", 3, 7, diagnostics));
            var adapter = new LumberYardConstructionSaleAdapter(yard);
            var requirement = new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-LUMBER",
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "Merchantable lumber",
                RequiredUnits = 5,
                UnitLabel = "boards",
                SupplierBusinessId = "yard-1",
            };

            string refusal = adapter.TrySellToProject("yard-1", "project-4", "package-frame",
                new List<ConstructionMaterialRequirement> { requirement }, new MaterialPrices(), 4,
                diagnostics, out ConstructionMaterialSale sale);

            Assert.IsNull(refusal);
            Assert.NotNull(sale);
            Assert.AreEqual(60, sale.TotalCents);
            Assert.AreEqual("project-4", sale.ProjectId);
            Assert.AreEqual(5, sale.Lines[0].Units);
            Assert.IsTrue(sale.Lines[0].Provenance.Contains("sawmill-1"));
            Assert.AreEqual(3, yard.LumberStock.AvailableUnitsForSale(null, null),
                "Five real units leave the yard; no inventory is created by the bridge.");
        }

        [Test]
        public void ConstructionProcurement_RejectsUnsupportedMaterialsBeforeConsumingYardStock()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diagnostics = new List<string>();
            Assert.IsNull(yard.ReceiveLumberPurchase(8, "white pine",
                LumberYardGradeCatalog.MerchantableGradeId, "sawmill-1", "mill sale 14", 3, 7, diagnostics));
            var adapter = new LumberYardConstructionSaleAdapter(yard);
            var requirement = new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-GLASS",
                MaterialKind = ConstructionMaterialKind.Glass,
                MaterialDisplayName = "Window glass",
                RequiredUnits = 2,
            };

            string refusal = adapter.TrySellToProject("yard-1", "project-4", "package-window",
                new List<ConstructionMaterialRequirement> { requirement }, new MaterialPrices(), 4,
                diagnostics, out ConstructionMaterialSale sale);

            Assert.IsNotNull(refusal);
            Assert.IsNull(sale);
            Assert.AreEqual(8, yard.LumberStock.AvailableUnitsForSale(null, null));
        }
    }
}
