using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass V P1-4: production consolidation. Every business travels ONE path
    /// (legacy archetype OR generic); the duplicate inventory truth (category
    /// stock vs generic positions) is retired by routing, with conserved
    /// consolidation when a business is found holding stock on the wrong source.
    /// </summary>
    [TestFixture]
    public sealed class ProductionPathConsolidationTests
    {
        [SetUp]
        public void ClearOverrides()
        {
            ProductionPathRegistry.ClearAllInstanceOverrides();
        }

        [TearDown]
        public void DropOverrides()
        {
            ProductionPathRegistry.ClearAllInstanceOverrides();
        }

        [Test]
        public void PathMap_CoversEveryBusinessTypeWithExactlyOnePath()
        {
            foreach (BusinessType type in (BusinessType[])Enum.GetValues(typeof(BusinessType)))
            {
                ProductionPath path = ProductionPathRegistry.GetPathForType(type);
                Assert.AreNotEqual(ProductionPath.Unspecified, path,
                    $"BusinessType.{type} has no production path — append-only enum values must be routed.");
                if (type == BusinessType.Generic)
                    Assert.AreEqual(ProductionPath.GenericPath, path);
                else
                    Assert.AreEqual(ProductionPath.LegacyArchetype, path,
                        $"BusinessType.{type} must stay on the legacy path unless explicitly migrated.");
            }
        }

        [Test]
        public void PathMap_InstanceOverrideWinsAndClears()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Bakery, "bakery-1");
            string id = business.InstanceId;
            Assert.AreEqual(ProductionPath.LegacyArchetype, ProductionPathRegistry.GetPath(business));
            ProductionPathRegistry.SetInstancePath(id, ProductionPath.GenericPath);
            Assert.AreEqual(ProductionPath.GenericPath, ProductionPathRegistry.GetPath(business));
            ProductionPathRegistry.ClearInstancePath(id);
            Assert.AreEqual(ProductionPath.LegacyArchetype, ProductionPathRegistry.GetPath(business));
        }

        [Test]
        public void Sale_GenericBusiness_ConsumesPositionsNeverCategoryStock()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Generic, "gen-1");
            ConfigureRetailLine(business, "flour", 20f);
            business.GenericConfiguration.AddInventory("flour", 4f, 10,
                new GenericProductDefinition("flour", "Flour", ProductQuantityUnit.Each), "test", 1);
            business.RuntimeState.EnsureCategoryStock("flour", 5, 10);

            var context = SaleContext(business, "flour", 2, 50);
            Assert.IsTrue(GenericRetailSaleAuthority.TryExecuteSale(business, context, _ => true, out string reason), reason);

            // The 5 stray category units consolidated into positions (4+5=9),
            // then the sale consumed 2 from positions. Category stock is zeroed.
            Assert.AreEqual(7f, business.GenericConfiguration.GetInventoryQuantity("flour"));
            Assert.AreEqual(0, business.RuntimeState.GetCategoryStock("flour").CurrentStockUnits);
        }

        [Test]
        public void Sale_LegacyBusiness_ConsumesCategoryStockNeverPositions()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Bakery, "bakery-2");
            ConfigureRetailLine(business, "bread", 20f);
            business.RuntimeState.EnsureCategoryStock("bread", 5, 10);
            business.GenericConfiguration.AddInventory("bread", 4f, 10,
                new GenericProductDefinition("bread", "Bread", ProductQuantityUnit.Each), "test", 1);

            var context = SaleContext(business, "bread", 2, 50);
            Assert.IsTrue(GenericRetailSaleAuthority.TryExecuteSale(business, context, _ => true, out string reason), reason);

            // The 4 stray position units consolidated into category stock
            // (5+4=9), then the sale consumed 2 from category stock.
            Assert.AreEqual(7, business.RuntimeState.GetCategoryStock("bread").CurrentStockUnits);
            Assert.AreEqual(0f, business.GenericConfiguration.GetInventoryQuantity("bread"));
        }

        [Test]
        public void ConsolidateInventory_ConservesUnitsExactly()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Generic, "gen-2");
            business.GenericConfiguration.AddInventory("nails", 3f, 5,
                new GenericProductDefinition("nails", "Nails", ProductQuantityUnit.Each), "test", 1);
            business.RuntimeState.EnsureCategoryStock("nails", 7, 20);

            float before = business.GenericConfiguration.GetInventoryQuantity("nails")
                + business.RuntimeState.GetCategoryStock("nails").CurrentStockUnits;
            var diag = new List<string>();
            int moved = ProductionPathRegistry.ConsolidateInventory(business, 4, diag);

            float after = business.GenericConfiguration.GetInventoryQuantity("nails")
                + business.RuntimeState.GetCategoryStock("nails").CurrentStockUnits;
            Assert.AreEqual(7, moved);
            Assert.AreEqual(before, after);
            Assert.AreEqual(0, business.RuntimeState.GetCategoryStock("nails").CurrentStockUnits);
            Assert.AreEqual(10f, business.GenericConfiguration.GetInventoryQuantity("nails"));
            Assert.IsTrue(diag.Count > 0);
        }

        [Test]
        public void ConsolidateInventory_IsIdempotent()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Generic, "gen-3");
            business.RuntimeState.EnsureCategoryStock("flour", 6, 10);
            Assert.AreEqual(6, ProductionPathRegistry.ConsolidateInventory(business, 4, null));
            Assert.AreEqual(0, ProductionPathRegistry.ConsolidateInventory(business, 4, null));
            Assert.AreEqual(6f, business.GenericConfiguration.GetInventoryQuantity("flour"));
        }

        [Test]
        public void GenericProduction_LegacyBusiness_IsGated()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Bakery, "bakery-3");
            RegisterTrivialMethod(business, "bake");
            var registry = new GenericProductionRuntimeRegistry(new TaskAuthority(), new WorkTimeBudgetStore());
            EntityId businessId = EntityId.For(EntityKind.Business, 77);

            bool started = registry.TryStartEligiblePolicy(business, businessId, 1,
                new[] { EntityId.For(EntityKind.Person, 5) }, out ProductionProcessState process, out string reason);

            Assert.IsFalse(started);
            Assert.IsNull(process);
            StringAssert.Contains("legacy", reason.ToLowerInvariant());

            registry.AdvanceBusinessProcesses(business, businessId, 1, 24 * 60);
            Assert.IsEmpty(business.GenericConfiguration.ActiveProcesses);
        }

        [Test]
        public void GenericProduction_GenericBusiness_StillSchedules()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Generic, "gen-4");
            RegisterTrivialMethod(business, "make");
            var registry = new GenericProductionRuntimeRegistry(new TaskAuthority(), new WorkTimeBudgetStore());
            EntityId businessId = EntityId.For(EntityKind.Business, 78);

            bool started = registry.TryStartEligiblePolicy(business, businessId, 1,
                new[] { EntityId.For(EntityKind.Person, 5) }, out ProductionProcessState process, out string reason);

            Assert.IsTrue(started, reason);
            Assert.IsNotNull(process);
        }

        [Test]
        public void ReservedSale_LegacyBusiness_IsRefusedLoudly()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.GeneralStore, "store-1");
            var line = new GenericRetailProductLine("flour", "storage", 5f, 10f, 2f, 50);
            line.SetSpecialOrderAllowed(true);
            business.TryConfigureGenericRetailLine(line, 10f);
            var context = SaleContext(business, "flour", 1, 50);
            Assert.IsFalse(GenericRetailSaleAuthority.TryExecuteReservedSale(business, context, _ => true, out string reason));
            StringAssert.Contains("legacy", reason.ToLowerInvariant());
        }

        // ---------- helpers ----------

        private static BusinessInstanceState CreateBusiness(BusinessType type, string instanceId)
        {
            // The generic BIZ-1 creation flow is not in the Roslyn harness;
            // attach the same state it would (type, id, blank runtime state).
            var business = new BusinessInstanceState();
            SetField(business, "instanceId", instanceId);
            SetField(business, "businessType", type);
            SetField(business, "runtimeState", new BusinessRuntimeState());
            return business;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "field '" + fieldName + "' not found");
            field.SetValue(target, value);
        }

        private static void ConfigureRetailLine(BusinessInstanceState business, string productId, float capacity)
        {
            business.TryConfigureGenericRetailLine(
                new GenericRetailProductLine(productId, "storage", 5f, capacity, 2f, 50), capacity);
            business.GenericConfiguration.Retail.TryOccupyProductSpace(productId, 0f);
        }

        private static GenericRetailSaleContext SaleContext(BusinessInstanceState business, string productId, int quantity, int unitPrice)
        {
            return new GenericRetailSaleContext
            {
                BuyerPrincipal = "household:7",
                ActingPersonId = 42,
                SellerBusinessId = business.InstanceId,
                ProductId = productId,
                Quantity = quantity,
                UnitPriceCents = unitPrice,
                LocationId = "shop-1",
                DayIndex = 3,
            };
        }

        private static void RegisterTrivialMethod(BusinessInstanceState business, string methodId)
        {
            business.RegisterGenericProductionMethod(new ProductionMethodDefinition
            {
                MethodId = methodId,
                OutputProductId = methodId + "-out",
                OutputQuantity = 1f,
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { ElapsedMinutes = 30, RequiresPerson = true },
                },
            });
            business.RegisterGenericProductionPolicy(new GenericProductionPolicy
            {
                MethodId = methodId,
                TargetStock = 10f,
                MaximumStock = 20f,
                Priority = 1,
            });
        }

    }
}
