using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LandLedgers.Persistence;
using LandLedgers.Economy.Blacksmith;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Product-level trade units. A ProductDefinition owns the unit used by
    /// Inventory; supplier packages and customer quantities may use another
    /// unit and must be converted explicitly by their terms.
    /// </summary>
    public enum ProductQuantityUnit
    {
        Each = 0,
        Pound = 1,
        Ounce = 2,
        Gallon = 3,
        Quart = 4,
        Bushel = 5,
        Yard = 6,
        Foot = 7,
        BoardFoot = 8,
        Sack = 9,
        Bale = 10,
        Barrel = 11,
        Keg = 12,
        Case = 13,
        Head = 14,
    }

    [Serializable]
    public sealed class GenericProductDefinition
    {
        [SerializeField] private string productId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private ProductQuantityUnit inventoryUnit;
        [SerializeField] private bool requiresLotIdentity;
        [SerializeField] private bool perishable;

        public string ProductId => productId ?? string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? ProductId : displayName;
        public ProductQuantityUnit InventoryUnit => inventoryUnit;
        public bool RequiresLotIdentity => requiresLotIdentity || perishable;
        public bool Perishable => perishable;

        public GenericProductDefinition() { }

        public GenericProductDefinition(string id, string name, ProductQuantityUnit unit,
            bool requiresLotIdentity = false, bool perishable = false)
        {
            productId = id ?? string.Empty;
            displayName = name ?? string.Empty;
            inventoryUnit = unit;
            this.requiresLotIdentity = requiresLotIdentity;
            this.perishable = perishable;
        }

        public static GenericProductDefinition FromLegacyItem(ItemDefinition item)
        {
            if (item == null) return null;
            return new GenericProductDefinition(
                item.ItemId,
                item.DisplayName,
                ParseUnit(item.Unit),
                false,
                string.Equals(item.Unit, "perishable", StringComparison.OrdinalIgnoreCase));
        }

        private static ProductQuantityUnit ParseUnit(string value)
        {
            if (Enum.TryParse(value, true, out ProductQuantityUnit parsed)) return parsed;
            if (string.Equals(value, "lb", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "lbs", StringComparison.OrdinalIgnoreCase)) return ProductQuantityUnit.Pound;
            if (string.Equals(value, "dozen", StringComparison.OrdinalIgnoreCase)) return ProductQuantityUnit.Each;
            return ProductQuantityUnit.Each;
        }
    }

    [Serializable]
    public sealed class ProductPackageDefinition
    {
        [SerializeField] private string packageId = string.Empty;
        [SerializeField] private string productId = string.Empty;
        [SerializeField] private ProductQuantityUnit packageUnit;
        [SerializeField, Min(0f)] private float quantityInInventoryUnits;
        [SerializeField, Min(0)] private int packagePriceCents;

        public string PackageId => packageId ?? string.Empty;
        public string ProductId => productId ?? string.Empty;
        public ProductQuantityUnit PackageUnit => packageUnit;
        public float QuantityInInventoryUnits => Mathf.Max(0f, quantityInInventoryUnits);
        public int PackagePriceCents => Mathf.Max(0, packagePriceCents);

        public ProductPackageDefinition() { }

        public ProductPackageDefinition(string id, string product, ProductQuantityUnit unit,
            float quantityInInventoryUnits, int priceCents)
        {
            packageId = id ?? string.Empty;
            productId = product ?? string.Empty;
            packageUnit = unit;
            this.quantityInInventoryUnits = Mathf.Max(0f, quantityInInventoryUnits);
            packagePriceCents = Mathf.Max(0, priceCents);
        }
    }

    /// <summary>Shared catalog authority. It describes goods; it does not own stock.</summary>
    public sealed class GenericProductCatalog
    {
        private readonly Dictionary<string, GenericProductDefinition> products =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<GenericProductDefinition> Products => products.Values;

        public bool Register(GenericProductDefinition product)
        {
            if (product == null || string.IsNullOrWhiteSpace(product.ProductId) || products.ContainsKey(product.ProductId))
                return false;
            products.Add(product.ProductId, product);
            return true;
        }

        public bool RegisterLegacyItem(ItemDefinition item) => Register(GenericProductDefinition.FromLegacyItem(item));

        public bool TryGet(string productId, out GenericProductDefinition product)
        {
            product = null;
            return !string.IsNullOrWhiteSpace(productId) && products.TryGetValue(productId, out product);
        }
    }

    [Serializable]
    public sealed class GenericInventoryLot
    {
        [SerializeField] private string lotId = string.Empty;
        [SerializeField] private string productId = string.Empty;
        [SerializeField, Min(0f)] private float quantity;
        [SerializeField, Min(0f)] private float reservedQuantity;
        [SerializeField, Min(0)] private int unitCostCents;
        [SerializeField] private string provenance = string.Empty;
        [SerializeField] private int createdDayIndex = -1;

        public string LotId => lotId ?? string.Empty;
        public string ProductId => productId ?? string.Empty;
        public float Quantity => Mathf.Max(0f, quantity);
        public float ReservedQuantity => Mathf.Clamp(reservedQuantity, 0f, Quantity);
        public float AvailableQuantity => Mathf.Max(0f, Quantity - ReservedQuantity);
        public int UnitCostCents => Mathf.Max(0, unitCostCents);
        public string Provenance => provenance ?? string.Empty;
        public int CreatedDayIndex => createdDayIndex;

        public GenericInventoryLot() { }

        public GenericInventoryLot(string id, string product, float amount, int costCents, string source, int dayIndex)
        {
            lotId = id ?? string.Empty;
            productId = product ?? string.Empty;
            quantity = Mathf.Max(0f, amount);
            unitCostCents = Mathf.Max(0, costCents);
            provenance = source ?? string.Empty;
            createdDayIndex = dayIndex;
        }

        public float Consume(float amount)
        {
            float consumed = Mathf.Clamp(amount, 0f, Quantity);
            quantity -= consumed;
            return consumed;
        }
    }

    /// <summary>
    /// Aggregate-first Inventory. Lots are optional evidence, not mandatory
    /// one-object-per-unit state. Homogeneous goods remain compact.
    /// </summary>
    [Serializable]
    public sealed class GenericInventoryPosition
    {
        [SerializeField] private string productId = string.Empty;
        [SerializeField, Min(0f)] private float quantity;
        [SerializeField, Min(0f)] private float reservedQuantity;
        [SerializeField, Min(0)] private int averageUnitCostCents;
        [SerializeField] private List<GenericInventoryLot> lots = new();

        public string ProductId => productId ?? string.Empty;
        public float Quantity => Mathf.Max(0f, quantity);
        public float ReservedQuantity => Mathf.Clamp(reservedQuantity, 0f, Quantity);
        public float AvailableQuantity => Mathf.Max(0f, Quantity - ReservedQuantity);
        public int AverageUnitCostCents => Mathf.Max(0, averageUnitCostCents);
        public IReadOnlyList<GenericInventoryLot> Lots => lots;

        public GenericInventoryPosition() { }
        public GenericInventoryPosition(string productId) { this.productId = productId ?? string.Empty; }

        public void Add(float amount, int unitCostCents, GenericProductDefinition definition,
            string provenance = "", int dayIndex = -1)
        {
            if (amount <= 0f) return;
            float previousValue = Quantity * AverageUnitCostCents;
            quantity += amount;
            averageUnitCostCents = Mathf.RoundToInt((previousValue + amount * Mathf.Max(0, unitCostCents)) / Quantity);
            if (definition != null && definition.RequiresLotIdentity)
            {
                lots ??= new List<GenericInventoryLot>();
                lots.Add(new GenericInventoryLot($"lot-{Guid.NewGuid():N}", ProductId, amount, unitCostCents, provenance, dayIndex));
            }
        }

        public bool TryConsume(float amount, out float consumed)
        {
            consumed = 0f;
            if (amount <= 0f || AvailableQuantity <= 0f || amount > AvailableQuantity + 0.001f) return false;
            consumed = amount;
            quantity -= consumed;
            if (lots != null)
            {
                float remaining = consumed;
                for (int i = 0; i < lots.Count && remaining > 0f; i++)
                    remaining -= lots[i].Consume(remaining);
                lots.RemoveAll(lot => lot == null || lot.Quantity <= 0f);
            }
            return consumed > 0f;
        }

        public bool TryReserve(float amount)
        {
            if (amount <= 0f || amount > AvailableQuantity + 0.001f) return false;
            reservedQuantity = Mathf.Min(Quantity, reservedQuantity + amount);
            return true;
        }

        public bool ReleaseReservation(float amount)
        {
            if (amount <= 0f || ReservedQuantity <= 0f) return false;
            reservedQuantity = Mathf.Max(0f, reservedQuantity - amount);
            return true;
        }

        public bool TryConsumeReserved(float amount, out float consumed)
        {
            consumed = 0f;
            if (amount <= 0f || amount > ReservedQuantity + 0.001f) return false;
            consumed = amount;
            reservedQuantity -= amount;
            quantity -= amount;
            if (lots != null)
            {
                float remaining = consumed;
                for (int i = 0; i < lots.Count && remaining > 0f; i++) remaining -= lots[i].Consume(remaining);
                lots.RemoveAll(lot => lot == null || lot.Quantity <= 0f);
            }
            return true;
        }
    }

    [Serializable]
    public sealed class RetailCapacityPool
    {
        [SerializeField] private string poolId = string.Empty;
        [SerializeField, Min(0f)] private float allocated;
        [SerializeField, Min(0f)] private float occupied;

        public string PoolId => poolId ?? string.Empty;
        public float Allocated => Mathf.Max(0f, allocated);
        public float Occupied => Mathf.Clamp(occupied, 0f, Allocated);
        public float Available => Mathf.Max(0f, Allocated - Occupied);

        public RetailCapacityPool() { }
        public RetailCapacityPool(string id, float allocation) { poolId = id ?? string.Empty; allocated = Mathf.Max(0f, allocation); }

        public bool SetAllocation(float value)
        {
            value = Mathf.Max(0f, value);
            if (value + 0.001f < Occupied) return false;
            allocated = value;
            return true;
        }

        public bool TryOccupy(float amount)
        {
            if (amount < 0f || amount > Available + 0.001f) return false;
            occupied += amount;
            return true;
        }

        public void Release(float amount) => occupied = Mathf.Clamp(occupied - Mathf.Max(0f, amount), 0f, Allocated);
    }

    [Serializable]
    public sealed class GenericRetailProductLine
    {
        [SerializeField] private string productId = string.Empty;
        [SerializeField] private bool enabledForSale = true;
        [SerializeField] private string capacityPoolId = "storage";
        [SerializeField, Min(0f)] private float targetStock;
        [SerializeField, Min(0f)] private float maximumStock;
        [SerializeField, Min(0f)] private float reorderPoint;
        [SerializeField, Min(0)] private int sellingPriceCents;
        [SerializeField] private bool specialOrderAllowed;

        public string ProductId => productId ?? string.Empty;
        public bool EnabledForSale => enabledForSale;
        public string CapacityPoolId => string.IsNullOrWhiteSpace(capacityPoolId) ? "storage" : capacityPoolId;
        public float TargetStock => Mathf.Max(0f, targetStock);
        public float MaximumStock => Mathf.Max(TargetStock, maximumStock);
        public float ReorderPoint => Mathf.Max(0f, reorderPoint);
        public int SellingPriceCents => Mathf.Max(0, sellingPriceCents);
        public bool SpecialOrderAllowed => specialOrderAllowed;

        public bool CanReorder(float availableStock) => EnabledForSale && availableStock <= ReorderPoint + 0.001f;
        public bool CanAcceptStock(float availableStock, float incomingQuantity) =>
            incomingQuantity >= 0f && availableStock + incomingQuantity <= MaximumStock + 0.001f;

        public GenericRetailProductLine() { }

        public GenericRetailProductLine(string productId, string poolId, float target, float maximum, float reorder, int priceCents)
        {
            this.productId = productId ?? string.Empty;
            capacityPoolId = string.IsNullOrWhiteSpace(poolId) ? "storage" : poolId;
            targetStock = Mathf.Max(0f, target);
            maximumStock = Mathf.Max(targetStock, maximum);
            reorderPoint = Mathf.Clamp(reorder, 0f, maximumStock);
            sellingPriceCents = Mathf.Max(0, priceCents);
        }

        public void SetEnabledForSale(bool enabled) => enabledForSale = enabled;

        public void ConfigureStockPolicy(float target, float maximum, float reorder)
        {
            targetStock = Mathf.Max(0f, target);
            maximumStock = Mathf.Max(targetStock, maximum);
            reorderPoint = Mathf.Clamp(reorder, 0f, maximumStock);
        }

        public void SetSellingPriceCents(int priceCents) => sellingPriceCents = Mathf.Max(0, priceCents);

        public void SetSpecialOrderAllowed(bool allowed) => specialOrderAllowed = allowed;
    }

    [Serializable]
    public sealed class GenericRetailConfiguration
    {
        [SerializeField] private List<RetailCapacityPool> capacityPools = new();
        [SerializeField] private List<GenericRetailProductLine> productLines = new();

        public IReadOnlyList<RetailCapacityPool> CapacityPools => capacityPools;
        public IReadOnlyList<GenericRetailProductLine> ProductLines => productLines;

        public RetailCapacityPool GetOrCreatePool(string poolId, float allocation = 0f)
        {
            RetailCapacityPool pool = capacityPools.FirstOrDefault(p => string.Equals(p.PoolId, poolId, StringComparison.OrdinalIgnoreCase));
            if (pool != null) return pool;
            pool = new RetailCapacityPool(poolId, allocation);
            capacityPools.Add(pool);
            return pool;
        }

        public void AddOrReplaceLine(GenericRetailProductLine line)
        {
            if (line == null || string.IsNullOrWhiteSpace(line.ProductId)) return;
            productLines.RemoveAll(existing => string.Equals(existing.ProductId, line.ProductId, StringComparison.OrdinalIgnoreCase));
            productLines.Add(line);
        }

        public bool TryGetLine(string productId, out GenericRetailProductLine line)
        {
            line = productLines.FirstOrDefault(candidate => string.Equals(candidate.ProductId, productId, StringComparison.OrdinalIgnoreCase));
            return line != null;
        }

        public bool TryOccupyProductSpace(string productId, float amount)
        {
            if (!TryGetLine(productId, out GenericRetailProductLine line)) return false;
            return GetOrCreatePool(line.CapacityPoolId).TryOccupy(amount);
        }

        public void ReleaseProductSpace(string productId, float amount)
        {
            if (!TryGetLine(productId, out GenericRetailProductLine line)) return;
            GetOrCreatePool(line.CapacityPoolId).Release(amount);
        }
    }

    [Serializable]
    public sealed class GenericEquipmentInstallationState
    {
        public string AssetId = string.Empty;
        public string BusinessId = string.Empty;
        public string WorkspaceId = string.Empty;
        public string LocationId = string.Empty;
        public bool RequiresSetup;
        public bool Installed;
        public int InstalledDayIndex = -1;

        public bool IsOperational => !RequiresSetup || Installed;

        public bool CompleteSetup(int dayIndex)
        {
            if (Installed) return false;
            Installed = true;
            InstalledDayIndex = dayIndex;
            return true;
        }
    }

    [Serializable]
    public sealed class SupplierOfferLine
    {
        public string ProductId = string.Empty;
        public float AvailableQuantity;
        public float PackageQuantity;
        public int PackagePriceCents;
        public int MinimumQuantity;
        public int MinimumOrderValueCents;
        public int FreightCents;
        public int HandlingCents;
        public int LeadTimeMinutes;
        public List<SupplierPriceTier> PriceTiers = new();

        public int UnitPriceCents => PackageQuantity <= 0f ? 0 : Mathf.RoundToInt(PackagePriceCents / PackageQuantity);

        public int GetUnitPriceCents(float quantity)
        {
            int result = UnitPriceCents;
            foreach (SupplierPriceTier tier in PriceTiers ?? new List<SupplierPriceTier>())
            {
                if (tier != null && quantity >= tier.MinimumQuantity)
                    result = Mathf.Max(0, tier.UnitPriceCents);
            }
            return result;
        }
    }

    [Serializable]
    public sealed class SupplierPriceTier
    {
        public float MinimumQuantity;
        public int UnitPriceCents;
    }

    [Serializable]
    public sealed class SupplierOffer
    {
        public string OfferId = string.Empty;
        public string SupplierId = string.Empty;
        public string SourceLocationId = string.Empty;
        public int ValidUntilDayIndex = -1;
        public int MinimumOrderQuantity;
        public int MinimumOrderValueCents;
        public List<SupplierOfferLine> Lines = new();
        public string PaymentTerms = string.Empty;

        public SupplierOfferLine FindLine(string productId) =>
            Lines.FirstOrDefault(line => line != null && string.Equals(line.ProductId, productId, StringComparison.OrdinalIgnoreCase));
    }

    [Serializable]
    public sealed class SupplierOrderLine
    {
        public string ProductId = string.Empty;
        public float Quantity;
        public float PackageQuantity;
        public int UnitPriceCents;
        public int LineTotalCents => Mathf.Max(0, Mathf.RoundToInt(Quantity * UnitPriceCents));
        public bool UsesWholePackages => PackageQuantity <= 0f
            || Mathf.Abs(Quantity / PackageQuantity - Mathf.Round(Quantity / PackageQuantity)) < 0.001f;
    }

    [Serializable]
    public sealed class SupplierPurchaseOrder
    {
        public string OrderId = string.Empty;
        public string SupplierId = string.Empty;
        public List<SupplierOrderLine> Lines = new();
        public int FreightCents;
        public int HandlingCents;
        public int MinimumOrderValueCents;
        public int MinimumQuantity;

        public int MerchandiseSubtotalCents => Lines.Where(line => line != null).Sum(line => line.LineTotalCents);
        public int TotalCents => MerchandiseSubtotalCents + Mathf.Max(0, FreightCents) + Mathf.Max(0, HandlingCents);
        public float TotalQuantity => Lines.Where(line => line != null).Sum(line => Mathf.Max(0f, line.Quantity));
        public bool MeetsMinimums => TotalQuantity >= Mathf.Max(0, MinimumQuantity)
            && MerchandiseSubtotalCents >= Mathf.Max(0, MinimumOrderValueCents)
            && (Lines ?? new List<SupplierOrderLine>()).Where(line => line != null).All(line => line.UsesWholePackages);
    }

    public enum EquipmentProcurementStage
    {
        Quoted = 0,
        Ordered = 1,
        PaidOrFinanced = 2,
        Shipped = 3,
        Received = 4,
        Installed = 5,
    }

    /// <summary>
    /// Small orchestration record for capital goods. It never creates an Asset or
    /// destination stock; it carries the ordinary supplier order/shipment stages
    /// until an existing EquipmentAsset is received and installed.
    /// </summary>
    [Serializable]
    public sealed class EquipmentProcurementState
    {
        public string AcquisitionId = string.Empty;
        public string BusinessId = string.Empty;
        public string SupplierId = string.Empty;
        public string ShipmentId = string.Empty;
        public string AssetId = string.Empty;
        public SupplierPurchaseOrder Order;
        public EquipmentProcurementStage Stage;
        public GenericEquipmentInstallationState Installation;

        public bool MarkOrdered()
        {
            if (Stage != EquipmentProcurementStage.Quoted || Order == null || !Order.MeetsMinimums) return false;
            Stage = EquipmentProcurementStage.Ordered;
            return true;
        }

        public bool MarkPaidOrFinanced()
        {
            if (Stage != EquipmentProcurementStage.Ordered) return false;
            Stage = EquipmentProcurementStage.PaidOrFinanced;
            return true;
        }

        public bool MarkShipped(string shipmentId)
        {
            if (Stage != EquipmentProcurementStage.PaidOrFinanced || string.IsNullOrWhiteSpace(shipmentId)) return false;
            ShipmentId = shipmentId;
            Stage = EquipmentProcurementStage.Shipped;
            return true;
        }

        public bool Receive(EquipmentAsset asset, string businessId, string locationId)
        {
            if (Stage != EquipmentProcurementStage.Shipped || asset == null || string.IsNullOrWhiteSpace(asset.AssetId)) return false;
            AssetId = asset.AssetId;
            BusinessId = businessId ?? string.Empty;
            asset.OwnerKind = "business";
            asset.OwnerId = BusinessId;
            asset.LocationId = locationId ?? string.Empty;
            Stage = EquipmentProcurementStage.Received;
            Installation ??= new GenericEquipmentInstallationState();
            Installation.AssetId = AssetId;
            Installation.BusinessId = BusinessId;
            Installation.LocationId = asset.LocationId;
            return true;
        }

        public bool Install(int dayIndex)
        {
            if (Stage != EquipmentProcurementStage.Received || Installation == null) return false;
            Installation.CompleteSetup(dayIndex);
            Stage = EquipmentProcurementStage.Installed;
            return true;
        }
    }

    /// <summary>Convenience preset only. It suggests configuration; it never creates
    /// equipment, inventory, premises, workers, or operating state.</summary>
    [Serializable]
    public sealed class GenericBusinessSetupTemplate
    {
        public string TemplateId = string.Empty;
        public string DisplayName = string.Empty;
        public List<string> SuggestedEquipmentKinds = new();
        public List<string> SuggestedProductIds = new();
        public List<string> SuggestedProductionMethodIds = new();
        public bool SuggestCustomerFacingSpace;

        public void ApplyPolicyOnly(GenericBusinessConfiguration configuration)
        {
            if (configuration == null) return;
            foreach (string productId in SuggestedProductIds ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(productId))
                    configuration.Retail.AddOrReplaceLine(new GenericRetailProductLine(productId, "storage", 0f, 0f, 0f, 0));
            }
        }
    }

    [Serializable]
    public sealed class GenericRetailSaleContext
    {
        public string BuyerPrincipal = string.Empty;
        public int ActingPersonId = -1;
        public string SellerBusinessId = string.Empty;
        public string ProductId = string.Empty;
        public int Quantity;
        public int UnitPriceCents;
        public string LocationId = string.Empty;
    }

    /// <summary>
    /// Shared retail execution seam. Inventory custody is changed here; the caller's
    /// existing Transaction/bookkeeping authority performs cash, credit, receivables,
    /// barter, and provenance settlement through the supplied context callback.
    /// </summary>
    public static class GenericRetailSaleAuthority
    {
        public static bool TryExecuteSale(
            BusinessInstanceState business,
            GenericRetailSaleContext context,
            Func<GenericRetailSaleContext, bool> settleTransaction,
            out string reason)
        {
            reason = string.Empty;
            if (business == null || business.RuntimeState == null || context == null)
            {
                reason = "Retail sale blocked: missing Business, runtime Inventory, or sale context.";
                return false;
            }
            if (!business.GenericConfiguration.Retail.TryGetLine(context.ProductId, out GenericRetailProductLine line)
                || !line.EnabledForSale)
            {
                reason = $"Retail sale blocked: '{context.ProductId}' is not enabled on this Business.";
                return false;
            }
            if (context.Quantity <= 0 || context.UnitPriceCents < 0)
            {
                reason = "Retail sale blocked: invalid quantity or price.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(context.BuyerPrincipal) || context.ActingPersonId < 0)
            {
                reason = "Retail sale blocked: a real BuyerPrincipal and ActingPerson are required.";
                return false;
            }
            bool genericInventorySale = business.GenericConfiguration.GetAvailableInventoryQuantity(context.ProductId)
                >= context.Quantity - 0.001f;
            int genericInventoryCost = genericInventorySale
                ? business.GenericConfiguration.GetInventoryAverageUnitCostCents(context.ProductId)
                : 0;
            int consumed = 0;
            float consumedGeneric = 0f;
            bool consumedSuccessfully = genericInventorySale
                ? business.GenericConfiguration.TryConsumeInventory(context.ProductId, context.Quantity, out consumedGeneric)
                : business.RuntimeState.TryConsumeCategoryStockUnits(context.ProductId, context.Quantity, out consumed);
            if (!consumedSuccessfully || (genericInventorySale ? consumedGeneric < context.Quantity - 0.001f : consumed != context.Quantity))
            {
                reason = $"Retail sale blocked: '{context.ProductId}' stock is insufficient.";
                return false;
            }

            if (!(settleTransaction?.Invoke(context) ?? false))
            {
                if (genericInventorySale)
                    business.GenericConfiguration.AddInventory(context.ProductId, context.Quantity, genericInventoryCost);
                else
                    business.RuntimeState.AddCategoryStockUnits(context.ProductId, consumed);
                reason = "Retail sale rolled back: Transaction authority refused settlement.";
                return false;
            }

            business.GenericConfiguration.Retail.ReleaseProductSpace(context.ProductId, context.Quantity);
            return true;
        }

        public static bool TryExecuteReservedSale(
            BusinessInstanceState business,
            GenericRetailSaleContext context,
            Func<GenericRetailSaleContext, bool> settleTransaction,
            out string reason)
        {
            reason = string.Empty;
            if (business == null || context == null || context.Quantity <= 0
                || string.IsNullOrWhiteSpace(context.BuyerPrincipal) || context.ActingPersonId < 0)
            {
                reason = "Reserved sale requires a real buyer, acting Person, and quantity.";
                return false;
            }
            if (!business.GenericConfiguration.Retail.TryGetLine(context.ProductId, out GenericRetailProductLine line)
                || !line.EnabledForSale || !line.SpecialOrderAllowed)
            {
                reason = "Product is not enabled for special-order retail.";
                return false;
            }
            if (!business.GenericConfiguration.TryConsumeReservedInventory(context.ProductId, context.Quantity, out float consumed)
                || consumed < context.Quantity - 0.001f)
            {
                reason = "Reserved special-order stock is unavailable.";
                return false;
            }
            if (!(settleTransaction?.Invoke(context) ?? false))
            {
                business.GenericConfiguration.AddInventory(context.ProductId, context.Quantity);
                business.GenericConfiguration.TryReserveInventory(context.ProductId, context.Quantity);
                reason = "Reserved sale rolled back: Transaction authority refused settlement.";
                return false;
            }
            business.GenericConfiguration.Retail.ReleaseProductSpace(context.ProductId, context.Quantity);
            return true;
        }
    }

    public enum CustomerSpecialOrderStage
    {
        Requested = 0,
        Quoted = 1,
        Ordered = 2,
        InTransit = 3,
        Received = 4,
        Collected = 5,
    }

    /// <summary>Special orders reuse supplier orders, shipments, receiving, and the
    /// ordinary retail Transaction seam; they do not create a hidden stock pool.</summary>
    [Serializable]
    public sealed class CustomerSpecialOrderState
    {
        public string OrderId = string.Empty;
        public string BuyerPrincipal = string.Empty;
        public int ActingPersonId = -1;
        public string SellerBusinessId = string.Empty;
        public SupplierOffer Offer;
        public SupplierPurchaseOrder PurchaseOrder;
        public string ShipmentId = string.Empty;
        public CustomerSpecialOrderStage Stage;

        public bool AcceptQuote(SupplierOffer offer)
        {
            if (Stage != CustomerSpecialOrderStage.Requested || offer == null || offer.Lines == null || offer.Lines.Count == 0) return false;
            Offer = offer;
            Stage = CustomerSpecialOrderStage.Quoted;
            return true;
        }

        public bool PlaceOrder(SupplierPurchaseOrder purchaseOrder)
        {
            if (Stage != CustomerSpecialOrderStage.Quoted || purchaseOrder == null || !purchaseOrder.MeetsMinimums) return false;
            PurchaseOrder = purchaseOrder;
            Stage = CustomerSpecialOrderStage.Ordered;
            return true;
        }

        public bool Dispatch(string shipmentId)
        {
            if (Stage != CustomerSpecialOrderStage.Ordered || string.IsNullOrWhiteSpace(shipmentId)) return false;
            ShipmentId = shipmentId;
            Stage = CustomerSpecialOrderStage.InTransit;
            return true;
        }

        public bool Receive() 
        {
            if (Stage != CustomerSpecialOrderStage.InTransit) return false;
            Stage = CustomerSpecialOrderStage.Received;
            return true;
        }

        public bool ReceiveIntoBusiness(BusinessInstanceState business, int landedUnitCostCents,
            GenericProductDefinition definition, int dayIndex)
        {
            if (Stage != CustomerSpecialOrderStage.InTransit || business == null || PurchaseOrder == null
                || PurchaseOrder.Lines == null || PurchaseOrder.Lines.Count == 0) return false;
            SupplierOrderLine line = PurchaseOrder.Lines[0];
            if (!business.GenericConfiguration.TryReceiveInventoryShipment(ShipmentId, line.ProductId,
                line.Quantity, landedUnitCostCents, definition, $"special-order:{OrderId}", dayIndex)) return false;
            if (!business.GenericConfiguration.TryReserveInventory(line.ProductId, line.Quantity))
            {
                business.GenericConfiguration.TryConsumeInventory(line.ProductId, line.Quantity, out _);
                return false;
            }
            Stage = CustomerSpecialOrderStage.Received;
            return true;
        }

        public bool CollectFromBusiness(BusinessInstanceState business, int unitPriceCents,
            Func<GenericRetailSaleContext, bool> transaction, out string reason)
        {
            reason = string.Empty;
            if (Stage != CustomerSpecialOrderStage.Received || business == null || PurchaseOrder == null
                || PurchaseOrder.Lines == null || PurchaseOrder.Lines.Count == 0) return false;
            SupplierOrderLine line = PurchaseOrder.Lines[0];
            var context = new GenericRetailSaleContext
            {
                BuyerPrincipal = BuyerPrincipal,
                ActingPersonId = ActingPersonId,
                SellerBusinessId = SellerBusinessId,
                ProductId = line.ProductId,
                Quantity = Mathf.RoundToInt(line.Quantity),
                UnitPriceCents = Mathf.Max(0, unitPriceCents),
            };
            if (!GenericRetailSaleAuthority.TryExecuteReservedSale(business, context, transaction, out reason)) return false;
            Stage = CustomerSpecialOrderStage.Collected;
            return true;
        }

        public bool Collect(Func<GenericRetailSaleContext, bool> transaction)
        {
            if (Stage != CustomerSpecialOrderStage.Received || transaction == null) return false;
            var context = new GenericRetailSaleContext
            {
                BuyerPrincipal = BuyerPrincipal,
                ActingPersonId = ActingPersonId,
                SellerBusinessId = SellerBusinessId,
                ProductId = PurchaseOrder != null && PurchaseOrder.Lines.Count > 0 ? PurchaseOrder.Lines[0].ProductId : string.Empty,
                Quantity = PurchaseOrder != null && PurchaseOrder.Lines.Count > 0 ? Mathf.RoundToInt(PurchaseOrder.Lines[0].Quantity) : 0,
            };
            if (!transaction(context)) return false;
            Stage = CustomerSpecialOrderStage.Collected;
            return true;
        }
    }

    [Serializable]
    public sealed class ProductionPhaseDefinition
    {
        public string PhaseId = string.Empty;
        public int ElapsedMinutes;
        public bool RequiresPerson;
        public List<string> ReservedEquipmentIds = new();
        public List<string> ReservedWorkspaceIds = new();
        public bool KeepsMaterialsOccupied = true;
    }

    [Serializable]
    public sealed class ProductionInputRequirement
    {
        public string ProductId = string.Empty;
        public float Quantity;
    }

    [Serializable]
    public sealed class ProductionMethodDefinition
    {
        public string MethodId = string.Empty;
        public string OutputProductId = string.Empty;
        public float OutputQuantity;
        public List<ProductionInputRequirement> InputRequirements = new();
        public List<string> RequiredEquipmentIds = new();
        public List<string> RequiredWorkspaceIds = new();
        public List<ProductionPhaseDefinition> Phases = new();
        public int Difficulty;
        /// <summary>Optional performance signal; never an attempt permission gate.</summary>
        public string PerformanceSkillId = string.Empty;
    }

    [Serializable]
    public sealed class ProductionProcessState
    {
        public string ProcessId = string.Empty;
        public string MethodId = string.Empty;
        public int CurrentPhaseIndex;
        public int RemainingPhaseMinutes;
        public bool Completed;
        public bool OutputCreated;
        public string AssignedPersonId = string.Empty;
        public List<string> ReservedEquipmentIds = new();
        public List<string> ReservedWorkspaceIds = new();
        public List<string> ConsumedInputIds = new();

        public bool PersonRequiredNow(ProductionMethodDefinition method)
        {
            if (Completed || method == null || CurrentPhaseIndex < 0 || CurrentPhaseIndex >= method.Phases.Count) return false;
            return method.Phases[CurrentPhaseIndex] != null && method.Phases[CurrentPhaseIndex].RequiresPerson;
        }

        public void Start(ProductionMethodDefinition method)
        {
            if (method == null || method.Phases == null || method.Phases.Count == 0) return;
            CurrentPhaseIndex = 0;
            RemainingPhaseMinutes = Mathf.Max(1, method.Phases[0].ElapsedMinutes);
            Completed = false;
            OutputCreated = false;
            ApplyCurrentPhaseReservations(method);
        }

        public void ApplyCurrentPhaseReservations(ProductionMethodDefinition method)
        {
            ReservedEquipmentIds.Clear();
            ReservedWorkspaceIds.Clear();
            if (Completed || method == null || method.Phases == null
                || CurrentPhaseIndex < 0 || CurrentPhaseIndex >= method.Phases.Count)
                return;

            ProductionPhaseDefinition phase = method.Phases[CurrentPhaseIndex];
            if (phase == null) return;
            if (phase.ReservedEquipmentIds != null) ReservedEquipmentIds.AddRange(phase.ReservedEquipmentIds);
            if (phase.ReservedWorkspaceIds != null) ReservedWorkspaceIds.AddRange(phase.ReservedWorkspaceIds);
        }

        public bool Advance(ProductionMethodDefinition method, int minutes)
        {
            if (Completed || method == null || minutes <= 0) return false;
            int remaining = minutes;
            while (remaining > 0 && !Completed)
            {
                if (RemainingPhaseMinutes <= 0) RemainingPhaseMinutes = Mathf.Max(1, method.Phases[CurrentPhaseIndex].ElapsedMinutes);
                int step = Mathf.Min(remaining, RemainingPhaseMinutes);
                RemainingPhaseMinutes -= step;
                remaining -= step;
                if (RemainingPhaseMinutes <= 0)
                {
                    CurrentPhaseIndex++;
                    if (CurrentPhaseIndex >= method.Phases.Count)
                    {
                        Completed = true;
                        ReservedEquipmentIds.Clear();
                        ReservedWorkspaceIds.Clear();
                        break;
                    }
                    RemainingPhaseMinutes = Mathf.Max(1, method.Phases[CurrentPhaseIndex].ElapsedMinutes);
                    ApplyCurrentPhaseReservations(method);
                }
            }
            return Completed;
        }

        public bool ConsumeInputsOnce(ProductionMethodDefinition method,
            Func<ProductionInputRequirement, bool> consume,
            Action<ProductionInputRequirement> rollback = null)
        {
            if (method == null || ConsumedInputIds == null || ConsumedInputIds.Contains(MethodId)) return false;
            var consumed = new List<ProductionInputRequirement>();
            foreach (ProductionInputRequirement input in method.InputRequirements ?? new List<ProductionInputRequirement>())
            {
                if (input == null || input.Quantity <= 0f) continue;
                if (!(consume?.Invoke(input) ?? false))
                {
                    for (int i = consumed.Count - 1; i >= 0; i--) rollback?.Invoke(consumed[i]);
                    return false;
                }
                consumed.Add(input);
            }
            ConsumedInputIds.Add(MethodId);
            return true;
        }
    }

    /// <summary>
    /// Physical possibility read model. This is deliberately independent of
    /// BusinessCapabilityRegistry and never grants an operation by itself.
    /// </summary>
    public static class PhysicalActivityEvaluator
    {
        public static string ExplainProductionPossibility(
            ProductionMethodDefinition method,
            Func<string, bool> hasUsableEquipment,
            Func<string, bool> hasWorkspace,
            Func<string, bool> hasInput,
            Func<string, bool> isPersonAvailable)
        {
            if (method == null) return "No production method selected.";
            foreach (string equipment in method.RequiredEquipmentIds ?? new List<string>())
                if (!(hasUsableEquipment?.Invoke(equipment) ?? false)) return $"Physically blocked: missing usable equipment '{equipment}'.";
            foreach (string workspace in method.RequiredWorkspaceIds ?? new List<string>())
                if (!(hasWorkspace?.Invoke(workspace) ?? false)) return $"Physically blocked: unavailable workspace '{workspace}'.";
            foreach (ProductionInputRequirement input in method.InputRequirements ?? new List<ProductionInputRequirement>())
            {
                if (input == null || string.IsNullOrWhiteSpace(input.ProductId) || input.Quantity <= 0f) continue;
                if (!(hasInput?.Invoke(input.ProductId) ?? false))
                    return $"Physically blocked: required input '{input.ProductId}' is unavailable.";
            }
            if (!(isPersonAvailable?.Invoke(string.Empty) ?? true)) return "Physically blocked: no available Person.";
            return string.Empty;
        }
    }

    /// <summary>
    /// Execution-facing physical-state adapter. It is intentionally callback based:
    /// callers supply the authoritative equipment, workspace, inventory, and Person
    /// authorities. No BusinessType, occupation, or capability registry value can
    /// authorize a method here.
    /// </summary>
    public static class GenericProductionAuthority
    {
        public static bool TryBegin(
            ProductionMethodDefinition method,
            string processId,
            string personId,
            Func<string, bool> hasUsableEquipment,
            Func<string, bool> hasWorkspace,
            Func<string, bool> hasInput,
            Func<string, bool> isPersonAvailable,
            out ProductionProcessState process,
            out string reason)
        {
            process = null;
            reason = PhysicalActivityEvaluator.ExplainProductionPossibility(
                method, hasUsableEquipment, hasWorkspace, hasInput, isPersonAvailable);
            if (!string.IsNullOrWhiteSpace(reason)) return false;
            if (method.Phases == null || method.Phases.Count == 0)
            {
                reason = "Physically blocked: production method has no process phases.";
                return false;
            }

            process = new ProductionProcessState
            {
                ProcessId = processId ?? string.Empty,
                MethodId = method.MethodId ?? string.Empty,
                AssignedPersonId = personId ?? string.Empty
            };
            process.Start(method);
            return true;
        }

        public static bool TryCreateOutputOnce(ProductionProcessState process, Func<bool> createOutput)
        {
            if (process == null || !process.Completed || process.OutputCreated) return false;
            if (!(createOutput?.Invoke() ?? false)) return false;
            process.OutputCreated = true;
            return true;
        }
    }

    /// <summary>
    /// Generic per-business configuration. It is authoritative configuration
    /// only for policies and references; supported activities remain derived from
    /// the physical authorities evaluated at execution time.
    /// </summary>
    [Serializable]
    public sealed class GenericBusinessConfiguration
    {
        [SerializeField] private GenericRetailConfiguration retail = new();
        [SerializeField] private List<string> equipmentAssetIds = new();
        [SerializeField] private List<string> workspaceIds = new();
        [SerializeField] private List<GenericEquipmentInstallationState> equipmentInstallations = new();
        [SerializeField] private List<EquipmentProcurementState> equipmentProcurements = new();
        [SerializeField] private List<ProductionMethodDefinition> productionMethods = new();
        [SerializeField] private List<GenericProductionPolicy> productionPolicies = new();
        [SerializeField] private List<ProductionProcessState> activeProcesses = new();
        [SerializeField] private List<GenericInventoryPosition> inventory = new();
        [SerializeField] private List<string> receivedShipmentIds = new();

        public GenericRetailConfiguration Retail => retail ??= new GenericRetailConfiguration();
        public IReadOnlyList<string> EquipmentAssetIds => equipmentAssetIds;
        public IReadOnlyList<string> WorkspaceIds => workspaceIds;
        public IReadOnlyList<GenericEquipmentInstallationState> EquipmentInstallations => equipmentInstallations;
        public IReadOnlyList<EquipmentProcurementState> EquipmentProcurements => equipmentProcurements;
        public IReadOnlyList<GenericInventoryPosition> Inventory => inventory;
        public IReadOnlyList<string> ReceivedShipmentIds => receivedShipmentIds;
        public IReadOnlyList<ProductionMethodDefinition> ProductionMethods => productionMethods;
        public IReadOnlyList<GenericProductionPolicy> ProductionPolicies => productionPolicies;
        public IReadOnlyList<ProductionProcessState> ActiveProcesses => activeProcesses;

        public void AddEquipmentReference(string assetId)
        {
            if (!string.IsNullOrWhiteSpace(assetId) && !equipmentAssetIds.Contains(assetId)) equipmentAssetIds.Add(assetId);
        }

        public void AddWorkspaceReference(string workspaceId)
        {
            if (!string.IsNullOrWhiteSpace(workspaceId) && !workspaceIds.Contains(workspaceId)) workspaceIds.Add(workspaceId);
        }

        public void AddEquipmentInstallation(GenericEquipmentInstallationState installation)
        {
            if (installation == null || string.IsNullOrWhiteSpace(installation.AssetId)) return;
            equipmentInstallations.RemoveAll(existing => string.Equals(existing.AssetId, installation.AssetId, StringComparison.OrdinalIgnoreCase));
            equipmentInstallations.Add(installation);
            AddEquipmentReference(installation.AssetId);
        }

        public void AddEquipmentProcurement(EquipmentProcurementState procurement)
        {
            if (procurement == null || string.IsNullOrWhiteSpace(procurement.AcquisitionId)) return;
            equipmentProcurements.RemoveAll(existing => string.Equals(existing.AcquisitionId, procurement.AcquisitionId, StringComparison.OrdinalIgnoreCase));
            equipmentProcurements.Add(procurement);
        }

        public void RegisterReceivedEquipment(EquipmentAsset asset, GenericEquipmentInstallationState installation)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId)) return;
            AddEquipmentReference(asset.AssetId);
            if (installation != null)
            {
                installation.AssetId = asset.AssetId;
                AddEquipmentInstallation(installation);
            }
        }

        public GenericInventoryPosition GetOrCreateInventory(string productId)
        {
            inventory ??= new List<GenericInventoryPosition>();
            GenericInventoryPosition position = inventory.FirstOrDefault(existing => existing != null
                && string.Equals(existing.ProductId, productId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (position != null) return position;
            position = new GenericInventoryPosition(productId);
            inventory.Add(position);
            return position;
        }

        public float GetInventoryQuantity(string productId)
        {
            inventory ??= new List<GenericInventoryPosition>();
            return inventory.FirstOrDefault(existing => existing != null
                && string.Equals(existing.ProductId, productId ?? string.Empty, StringComparison.OrdinalIgnoreCase))?.Quantity ?? 0f;
        }

        public float GetAvailableInventoryQuantity(string productId)
        {
            inventory ??= new List<GenericInventoryPosition>();
            return inventory.FirstOrDefault(existing => existing != null
                && string.Equals(existing.ProductId, productId ?? string.Empty, StringComparison.OrdinalIgnoreCase))?.AvailableQuantity ?? 0f;
        }

        public int GetInventoryAverageUnitCostCents(string productId)
        {
            inventory ??= new List<GenericInventoryPosition>();
            return inventory.FirstOrDefault(existing => existing != null
                && string.Equals(existing.ProductId, productId ?? string.Empty, StringComparison.OrdinalIgnoreCase))?.AverageUnitCostCents ?? 0;
        }

        public void AddInventory(string productId, float quantity, int unitCostCents = 0,
            GenericProductDefinition definition = null, string provenance = "", int dayIndex = -1)
        {
            if (quantity <= 0f || string.IsNullOrWhiteSpace(productId)) return;
            GetOrCreateInventory(productId).Add(quantity, unitCostCents, definition, provenance, dayIndex);
        }

        public bool TryAddInventory(string productId, float quantity, int unitCostCents = 0,
            GenericProductDefinition definition = null, string provenance = "", int dayIndex = -1)
        {
            if (quantity <= 0f || string.IsNullOrWhiteSpace(productId)) return false;
            if (Retail.TryGetLine(productId, out _)
                && !Retail.TryOccupyProductSpace(productId, quantity)) return false;
            GetOrCreateInventory(productId).Add(quantity, unitCostCents, definition, provenance, dayIndex);
            return true;
        }

        public bool TryReceiveInventoryShipment(string shipmentId, string productId, float quantity,
            int unitCostCents, GenericProductDefinition definition, string provenance, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(shipmentId) || quantity <= 0f) return false;
            receivedShipmentIds ??= new List<string>();
            if (receivedShipmentIds.Contains(shipmentId)) return false;
            if (!TryAddInventory(productId, quantity, unitCostCents, definition, provenance, dayIndex)) return false;
            receivedShipmentIds.Add(shipmentId);
            return true;
        }

        public bool TryConsumeInventory(string productId, float quantity, out float consumed)
        {
            consumed = 0f;
            inventory ??= new List<GenericInventoryPosition>();
            GenericInventoryPosition position = inventory.FirstOrDefault(existing => existing != null
                && string.Equals(existing.ProductId, productId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (position == null || quantity <= 0f || position.Quantity + 0.001f < quantity) return false;
            return position.TryConsume(quantity, out consumed) && consumed >= quantity - 0.001f;
        }

        public bool TryReserveInventory(string productId, float quantity)
        {
            inventory ??= new List<GenericInventoryPosition>();
            GenericInventoryPosition position = inventory.FirstOrDefault(existing => existing != null
                && string.Equals(existing.ProductId, productId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            return position != null && position.TryReserve(quantity);
        }

        public bool TryConsumeReservedInventory(string productId, float quantity, out float consumed)
        {
            consumed = 0f;
            inventory ??= new List<GenericInventoryPosition>();
            GenericInventoryPosition position = inventory.FirstOrDefault(existing => existing != null
                && string.Equals(existing.ProductId, productId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            return position != null && position.TryConsumeReserved(quantity, out consumed);
        }

        public bool ReleaseInventoryReservation(string productId, float quantity)
        {
            inventory ??= new List<GenericInventoryPosition>();
            GenericInventoryPosition position = inventory.FirstOrDefault(existing => existing != null
                && string.Equals(existing.ProductId, productId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            return position != null && position.ReleaseReservation(quantity);
        }

        public void AddProductionMethod(ProductionMethodDefinition method)
        {
            if (method == null || string.IsNullOrWhiteSpace(method.MethodId)) return;
            productionMethods.RemoveAll(existing => string.Equals(existing.MethodId, method.MethodId, StringComparison.OrdinalIgnoreCase));
            productionMethods.Add(method);
        }

        public void AddOrReplaceProductionPolicy(GenericProductionPolicy policy)
        {
            if (policy == null || string.IsNullOrWhiteSpace(policy.MethodId)) return;
            productionPolicies.RemoveAll(existing => string.Equals(existing.MethodId, policy.MethodId, StringComparison.OrdinalIgnoreCase));
            productionPolicies.Add(policy);
        }

        public bool TryGetProductionPolicy(string methodId, out GenericProductionPolicy policy)
        {
            policy = productionPolicies.FirstOrDefault(existing => existing != null
                && string.Equals(existing.MethodId, methodId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            return policy != null;
        }

        public void AddProcess(ProductionProcessState process)
        {
            if (process == null || string.IsNullOrWhiteSpace(process.ProcessId)) return;
            activeProcesses.RemoveAll(existing => string.Equals(existing.ProcessId, process.ProcessId, StringComparison.OrdinalIgnoreCase));
            activeProcesses.Add(process);
        }

        public bool IsEquipmentAvailable(string equipmentId)
        {
            if (string.IsNullOrWhiteSpace(equipmentId)
                || !(equipmentAssetIds ?? new List<string>()).Any(assetId =>
                    string.Equals(assetId, equipmentId, StringComparison.OrdinalIgnoreCase))) return false;
            return !(activeProcesses ?? new List<ProductionProcessState>()).Any(process =>
                process != null && !process.Completed
                && (process.ReservedEquipmentIds ?? new List<string>()).Any(reserved =>
                    string.Equals(reserved, equipmentId, StringComparison.OrdinalIgnoreCase)));
        }

        public bool IsWorkspaceAvailable(string workspaceId)
        {
            if (string.IsNullOrWhiteSpace(workspaceId)
                || !(workspaceIds ?? new List<string>()).Any(existing =>
                    string.Equals(existing, workspaceId, StringComparison.OrdinalIgnoreCase))) return false;
            return !(activeProcesses ?? new List<ProductionProcessState>()).Any(process =>
                process != null && !process.Completed
                && (process.ReservedWorkspaceIds ?? new List<string>()).Any(reserved =>
                    string.Equals(reserved, workspaceId, StringComparison.OrdinalIgnoreCase)));
        }

        public GenericBusinessConfigurationSaveDto CaptureSaveDto()
        {
            return new GenericBusinessConfigurationSaveDto
            {
                retail = retail,
                equipmentAssetIds = new List<string>(equipmentAssetIds ?? new List<string>()),
                workspaceIds = new List<string>(workspaceIds ?? new List<string>()),
                equipmentInstallations = new List<GenericEquipmentInstallationState>(equipmentInstallations ?? new List<GenericEquipmentInstallationState>()),
                equipmentProcurements = new List<EquipmentProcurementState>(equipmentProcurements ?? new List<EquipmentProcurementState>()),
                inventory = new List<GenericInventoryPosition>(inventory ?? new List<GenericInventoryPosition>()),
                receivedShipmentIds = new List<string>(receivedShipmentIds ?? new List<string>()),
                productionMethods = new List<ProductionMethodDefinition>(productionMethods ?? new List<ProductionMethodDefinition>()),
                productionPolicies = new List<GenericProductionPolicy>(productionPolicies ?? new List<GenericProductionPolicy>()),
                activeProcesses = new List<ProductionProcessState>(activeProcesses ?? new List<ProductionProcessState>())
            };
        }

        public static GenericBusinessConfiguration FromSaveDto(GenericBusinessConfigurationSaveDto dto)
        {
            var configuration = new GenericBusinessConfiguration();
            if (dto == null) return configuration;
            configuration.retail = dto.retail ?? new GenericRetailConfiguration();
            configuration.equipmentAssetIds = dto.equipmentAssetIds ?? new List<string>();
            configuration.workspaceIds = dto.workspaceIds ?? new List<string>();
            configuration.equipmentInstallations = dto.equipmentInstallations ?? new List<GenericEquipmentInstallationState>();
            configuration.equipmentProcurements = dto.equipmentProcurements ?? new List<EquipmentProcurementState>();
            configuration.inventory = dto.inventory ?? new List<GenericInventoryPosition>();
            configuration.receivedShipmentIds = dto.receivedShipmentIds ?? new List<string>();
            configuration.productionMethods = dto.productionMethods ?? new List<ProductionMethodDefinition>();
            configuration.productionPolicies = dto.productionPolicies ?? new List<GenericProductionPolicy>();
            configuration.activeProcesses = dto.activeProcesses ?? new List<ProductionProcessState>();
            return configuration;
        }
    }

    [Serializable]
    public sealed class GenericBusinessConfigurationSaveDto
    {
        public GenericRetailConfiguration retail = new();
        public List<string> equipmentAssetIds = new();
        public List<string> workspaceIds = new();
        public List<GenericEquipmentInstallationState> equipmentInstallations = new();
        public List<EquipmentProcurementState> equipmentProcurements = new();
        public List<GenericInventoryPosition> inventory = new();
        public List<string> receivedShipmentIds = new();
        public List<ProductionMethodDefinition> productionMethods = new();
        public List<GenericProductionPolicy> productionPolicies = new();
        public List<ProductionProcessState> activeProcesses = new();
    }
}
