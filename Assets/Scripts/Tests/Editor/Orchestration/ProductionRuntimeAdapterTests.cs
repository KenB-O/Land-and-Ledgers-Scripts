using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    public sealed class ProductionRuntimeAdapterTests
    {
        [Test]
        public void HouseholdConstructionMaterialSource_ConsumesAuthoritativeLotsAndPreservesProvenance()
        {
            var inventory = new HouseholdInventory(41);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 8, 2, "mill shipment 17"));

            var source = new HouseholdConstructionMaterialSource(inventory);
            var requirement = new ConstructionMaterialRequirement
            {
                MaterialDisplayName = "Flour",
                RequiredUnits = 3,
                UnitLabel = "lb",
            };

            Assert.AreEqual(8, source.AvailableUnits(requirement));
            string provenance;
            string refusal = source.TryConsume(requirement, 3, "project c-1", 3,
                new List<string>(), out provenance);

            Assert.IsNull(refusal);
            Assert.AreEqual(5, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId));
            StringAssert.Contains("mill shipment 17", provenance);
        }

        [Test]
        public void HouseholdInventoryToolCustody_ReadsHeldGoodsWithoutCreatingStock()
        {
            var registry = new HouseholdInventoryRegistry();
            HouseholdInventory inventory = registry.GetOrCreate(42);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FuelWoodId, 1, 1, "fuel dealer"));

            var custody = new HouseholdInventoryToolCustody(registry);
            Assert.IsTrue(custody.HouseholdHoldsTool(42, HouseholdItemCatalog.FuelWoodId));
            Assert.IsFalse(custody.HouseholdHoldsTool(99, HouseholdItemCatalog.FuelWoodId));
            Assert.IsFalse(custody.HouseholdHoldsTool(42, "unowned-tool"));
        }
    }
}
