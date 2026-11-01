using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Restaurant
{
    /// <summary>
    /// W2B: one pantry lot with full upstream provenance. Every pound of
    /// meat, every loaf, every produce unit, every dairy unit must trace to
    /// a real lot from a real supplier — the butcher's carcass chain, the
    /// bakery's bread lots, a farm's produce lots, the dairy's milk/butter
    /// with cow provenance, a declared import order from a named off-map
    /// origin, or a named local supply relationship — or to the explicit
    /// one-time bootstrap endowment. No orphan lots, no synthetic stock
    /// (upstream-provenance doctrine).
    /// </summary>
    [Serializable]
    public sealed class RestaurantFoodLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string FoodName = string.Empty; // restaurant-meat / restaurant-bread / restaurant-produce / restaurant-dairy
        public int Units; // lb of meat, loaves, produce units, dairy units
        public int AcquiredDayIndex;

        /// <summary>The named supplier business (butcher, bakery, farm, dairy) that sold this lot in.</summary>
        public string SupplierBusinessId = string.Empty;

        /// <summary>The supplier's own lot/batch id this lot was taken from (butcher lot id, bread lot id, produce lot id, milk lot / butter batch id).</summary>
        public string SourceLotId = string.Empty;

        /// <summary>The declared trade link that brought this lot in (ImportOrder.OrderId, or a local supply relationship id).</summary>
        public string ImportOrderId = string.Empty;

        /// <summary>Named off-map or local origin, e.g. "Off-map provision wholesaler, via railhead".</summary>
        public string OriginName = string.Empty;

        /// <summary>Any extra upstream link (source animal key, cow id, farm name, milled-by, local purchase).</summary>
        public string SupplierNote = string.Empty;

        /// <summary>
        /// True only for the one-time opening endowment applied by
        /// <see cref="RestaurantFoodBootstrap"/>. Explicitly marked, never
        /// silently replenished — reorder goes through real suppliers and
        /// import orders only.
        /// </summary>
        public bool IsBootstrapEndowment;

        public RestaurantFoodLot() { }

        /// <summary>Human-readable upstream chain for ledgers and diagnostics.</summary>
        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
            {
                return $"BOOTSTRAP endowment (lot {LotId}, day {AcquiredDayIndex}) — one-time opening stock; reorder via real suppliers and import orders";
            }

            string chain = $"lot {LotId} / day {AcquiredDayIndex}";
            if (!string.IsNullOrWhiteSpace(SupplierBusinessId))
            {
                chain += $" / supplier {SupplierBusinessId}";
                if (!string.IsNullOrWhiteSpace(SourceLotId))
                {
                    chain += $" ← supplier lot {SourceLotId}";
                }
            }

            string order = string.IsNullOrWhiteSpace(ImportOrderId) ? "no-order" : ImportOrderId;
            string origin = string.IsNullOrWhiteSpace(OriginName) ? "unnamed-origin" : OriginName;
            chain += $" / import order {order} / {origin}";
            if (!string.IsNullOrWhiteSpace(SupplierNote))
            {
                chain += $" / {SupplierNote}";
            }

            return chain;
        }
    }

    /// <summary>
    /// W2B: one line of a dispense result — which lot the units came from and
    /// the full upstream chain. Meal batches carry these so every ingredient
    /// is traceable into the served meal.
    /// </summary>
    [Serializable]
    public sealed class RestaurantFoodDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string FoodName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public RestaurantFoodDispenseLine() { }
    }

    /// <summary>
    /// W2B: the restaurant's pantry (meat, bread, produce, dairy). Units
    /// dispense FIFO (oldest stock first); every dispense returns the lots
    /// consumed so meal batches carry provenance. Refusals are loud —
    /// stock shortfalls are never faked.
    /// </summary>
    public sealed class RestaurantFoodStock
    {
        private readonly List<RestaurantFoodLot> lots = new List<RestaurantFoodLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<RestaurantFoodLot> Lots => lots;

        /// <summary>
        /// Receives a lot into the pantry. Lots must name their supplier chain
        /// (supplier business + source lot, or import order + origin) or carry
        /// the explicit bootstrap flag. Returns a rejection string, or null on
        /// success.
        /// </summary>
        public string ReceiveLot(RestaurantFoodLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null) return "RestaurantFoodStock: null lot refused — no food without a lot record.";
            if (string.IsNullOrWhiteSpace(lot.FoodName))
                return "RestaurantFoodStock: lot refused — food name is required.";
            if (lot.Units <= 0)
                return $"RestaurantFoodStock: lot refused — '{lot.FoodName}' needs a positive unit count.";
            if (!lot.IsBootstrapEndowment
                && string.IsNullOrWhiteSpace(lot.SupplierBusinessId)
                && (string.IsNullOrWhiteSpace(lot.ImportOrderId) || string.IsNullOrWhiteSpace(lot.OriginName)))
                return $"RestaurantFoodStock: lot refused — '{lot.FoodName}' names no supplier and no import order/origin. No orphan lots.";
            if (lot.LotId.IsValid)
            {
                foreach (var existing in lots)
                {
                    if (existing.LotId.Equals(lot.LotId))
                        return $"RestaurantFoodStock: lot refused — lot {lot.LotId} already in the pantry.";
                }
            }

            lots.Add(lot);
            diag.Add($"RestaurantFoodStock: received {lot.Units} units '{lot.FoodName}' — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses units FIFO (oldest first). Returns null with a diagnostic
        /// when the pantry cannot cover the request — no units are conjured.
        /// </summary>
        public List<RestaurantFoodDispenseLine> TryDispenseUnits(string foodName, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<RestaurantFoodDispenseLine>();
            if (string.IsNullOrWhiteSpace(foodName) || units <= 0)
            {
                diag.Add("RestaurantFoodStock: dispense refused — food name and positive unit count required.");
                return null;
            }

            int available = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.FoodName, foodName, StringComparison.OrdinalIgnoreCase))
                {
                    available += lot.Units;
                }
            }

            if (available < units)
            {
                diag.Add($"RestaurantFoodStock: only {available} units of '{foodName}' on hand, need {units} — shortfall, no units conjured.");
                return null;
            }

            // FIFO by acquisition day.
            var ordered = new List<RestaurantFoodLot>();
            foreach (var lot in lots)
            {
                if (string.Equals(lot.FoodName, foodName, StringComparison.OrdinalIgnoreCase))
                {
                    ordered.Add(lot);
                }
            }

            ordered.Sort((a, b) => a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex));

            int remaining = units;
            foreach (var lot in ordered)
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, lot.Units);
                lot.Units -= take;
                remaining -= take;
                lines.Add(new RestaurantFoodDispenseLine
                {
                    LotId = lot.LotId,
                    FoodName = lot.FoodName,
                    UnitsTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            // Drop emptied lots so the pantry never carries ghost stock.
            lots.RemoveAll(l => l.Units <= 0);

            diag.Add($"RestaurantFoodStock: dispensed {units} units '{foodName}' (day {dayIndex}) from {lines.Count} lot(s).");
            return lines;
        }

        /// <summary>Counts all on-hand units of a food kind.</summary>
        public int UnitsOnHand(string foodName)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.FoodName, foodName, StringComparison.OrdinalIgnoreCase))
                {
                    total += lot.Units;
                }
            }

            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class RestaurantFoodStockSaveDto
        {
            public List<RestaurantFoodLot> Lots = new List<RestaurantFoodLot>();
        }

        public RestaurantFoodStockSaveDto CaptureSaveDto()
        {
            var dto = new RestaurantFoodStockSaveDto();
            dto.Lots.AddRange(lots);
            return dto;
        }

        public void LoadFromSaveDto(RestaurantFoodStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            lots.AddRange(dto.Lots);
        }
        #endregion
    }

    /// <summary>
    /// W2B: the one-time opening endowment of the restaurant's pantry.
    /// Explicitly marked BOOTSTRAP — it stands in for the eating house's
    /// opening stocking and is NEVER silently replenished: later stock comes
    /// only from real suppliers (butcher, bakery, farms, dairy) or import
    /// orders of the <see cref="RestaurantFoodSupply"/> material ids.
    /// </summary>
    public static class RestaurantFoodBootstrap
    {
        /// <summary>TUNING: opening meat (lb) in the pantry.</summary>
        public const int BootstrapMeatUnits = 40;

        /// <summary>TUNING: opening bread loaves in the pantry.</summary>
        public const int BootstrapBreadUnits = 24;

        /// <summary>TUNING: opening produce units in the pantry.</summary>
        public const int BootstrapProduceUnits = 30;

        /// <summary>TUNING: opening dairy units in the pantry.</summary>
        public const int BootstrapDairyUnits = 16;

        public static void ApplyBootstrapEndowment(
            RestaurantFoodStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("RestaurantFoodBootstrap: no pantry — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("RestaurantFoodBootstrap: no id registry — endowment not applied.");
                return;
            }

            var endowments = new (string foodName, int units)[]
            {
                (RestaurantMealCatalog.MeatItemId, BootstrapMeatUnits),
                (RestaurantMealCatalog.BreadItemId, BootstrapBreadUnits),
                (RestaurantMealCatalog.ProduceItemId, BootstrapProduceUnits),
                (RestaurantMealCatalog.DairyItemId, BootstrapDairyUnits),
            };

            foreach (var (foodName, units) in endowments)
            {
                string rejection = stock.ReceiveLot(new RestaurantFoodLot
                {
                    LotId = idRegistry.Allocate(EntityKind.Lot),
                    FoodName = foodName,
                    Units = units,
                    AcquiredDayIndex = dayIndex,
                    IsBootstrapEndowment = true,
                    SupplierNote = "Opening eating-house stocking (one-time; reorder via real suppliers and import orders only).",
                }, diagnostics);
                if (rejection != null)
                {
                    diagnostics.Add($"RestaurantFoodBootstrap: {rejection}");
                    return;
                }
            }

            diagnostics.Add("RestaurantFoodBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: these lots are explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// W2B: the pantry's supply link. Survey result: four honest paths into
    /// the pantry, each preserving the supplier's own provenance —
    /// <list type="bullet">
    /// <item>the butcher's <see cref="ButcherLot"/> meat (carcass chain: source animal, produced day, condition at receipt);</item>
    /// <item>the bakery's <see cref="BakeryBreadLot"/> loaves (baked day, flour provenance baked in);</item>
    /// <item>farm <see cref="FarmProduceLot"/> produce (farm business id/name, produced day);</item>
    /// <item>dairy <see cref="MilkLot"/> milk or butter batches (cow provenance, churn batch).</item>
    /// </list>
    /// For towns missing a supplier, the honest fallback is a DECLARED off-map
    /// trade link (EQU-1 precedent: named origin, real distance and transit
    /// days). The shop reorders through <see cref="ImportService"/>.
    /// </summary>
    public static class RestaurantFoodSupply
    {
        public const string MeatMaterialId = "restaurant-meat";
        public const string BreadMaterialId = "restaurant-bread";
        public const string ProduceMaterialId = "restaurant-produce";
        public const string DairyMaterialId = "restaurant-dairy";

        /// <summary>
        /// Registers the restaurant's ingredients as importable materials
        /// (EQU-1 extension path). Prices/transit are calibration (Canon Part XV).
        /// </summary>
        public static void EnsureRestaurantImportables(List<string> diag)
        {
            Register(MeatMaterialId, "Meat (by the pound)", 250, 14, 12, diag);
            Register(BreadMaterialId, "Bread loaves", 250, 14, 8, diag);
            Register(ProduceMaterialId, "Farm produce", 250, 14, 5, diag);
            Register(DairyMaterialId, "Dairy (milk/butter)", 250, 14, 10, diag);
        }

        private static void Register(string materialId, string displayName, int distanceMiles, int transitDays, int pricePerUnitCents, List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                materialId, displayName, "Off-map provision wholesaler, via railhead",
                distanceMiles, transitDays, pricePerUnitCents));
            if (problem != null && diag != null)
            {
                diag.Add($"RestaurantFoodSupply: '{materialId}' import registration: {problem}");
            }
        }

        /// <summary>
        /// Takes lbs of meat from a butcher's lot into the pantry. The
        /// carcass chain (source animal, produced day, condition at receipt)
        /// is preserved on the pantry lot — the caller takes the lbs on the
        /// butcher's side; this records custody with provenance.
        /// </summary>
        public static string ReceiveButcherLot(
            RestaurantFoodStock stock,
            ButcherLot butcherLot,
            int lbs,
            string butcherBusinessId,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "RestaurantFoodSupply.ReceiveButcherLot: no pantry.";
            if (butcherLot == null || lbs <= 0)
                return "RestaurantFoodSupply.ReceiveButcherLot: no butcher lot offered — meat is not conjured.";
            if (string.IsNullOrWhiteSpace(butcherBusinessId))
                return "RestaurantFoodSupply.ReceiveButcherLot: the butcher business must be named — no orphan stock.";
            if (idRegistry == null) return "RestaurantFoodSupply.ReceiveButcherLot: no id registry.";

            return stock.ReceiveLot(new RestaurantFoodLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FoodName = MeatMaterialId,
                Units = lbs,
                AcquiredDayIndex = dayIndex,
                SupplierBusinessId = butcherBusinessId,
                SourceLotId = butcherLot.LotId,
                SupplierNote = $"{butcherLot.Product} from animal {butcherLot.SourceAnimalKey}, " +
                    $"produced day {butcherLot.ProducedDayIndex}, received at condition {butcherLot.Condition}",
                IsBootstrapEndowment = false,
            }, diag);
        }

        /// <summary>
        /// Takes loaves from a bakery's bread lot into the pantry. The bread
        /// lot's baked day and baked-in flour provenance ride along.
        /// </summary>
        public static string ReceiveBakeryBreadLot(
            RestaurantFoodStock stock,
            BakeryBreadLot breadLot,
            int loaves,
            string bakeryBusinessId,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "RestaurantFoodSupply.ReceiveBakeryBreadLot: no pantry.";
            if (breadLot == null || loaves <= 0)
                return "RestaurantFoodSupply.ReceiveBakeryBreadLot: no bread lot offered — bread is not conjured.";
            if (!breadLot.CanSell)
                return $"RestaurantFoodSupply.ReceiveBakeryBreadLot: bread lot {breadLot.LotId} is {breadLot.Condition} — stale bread never enters the pantry.";
            if (string.IsNullOrWhiteSpace(bakeryBusinessId))
                return "RestaurantFoodSupply.ReceiveBakeryBreadLot: the bakery business must be named — no orphan stock.";
            if (idRegistry == null) return "RestaurantFoodSupply.ReceiveBakeryBreadLot: no id registry.";

            return stock.ReceiveLot(new RestaurantFoodLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FoodName = BreadMaterialId,
                Units = loaves,
                AcquiredDayIndex = dayIndex,
                SupplierBusinessId = bakeryBusinessId,
                SourceLotId = breadLot.LotId.ToString(),
                SupplierNote = $"{breadLot.ProductId} baked day {breadLot.BakedDayIndex}, received {breadLot.Condition}; " +
                    "flour provenance: " + string.Join(" | ", breadLot.InputProvenance ?? new List<string>()),
                IsBootstrapEndowment = false,
            }, diag);
        }

        /// <summary>
        /// Takes produce units from a farm's produce lot into the pantry.
        /// Farm provenance (farm business id/name, produced day) is preserved.
        /// </summary>
        public static string ReceiveFarmProduceLot(
            RestaurantFoodStock stock,
            FarmProduceLot produceLot,
            int units,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "RestaurantFoodSupply.ReceiveFarmProduceLot: no pantry.";
            if (produceLot == null || units <= 0)
                return "RestaurantFoodSupply.ReceiveFarmProduceLot: no produce lot offered — produce is not conjured.";
            if (idRegistry == null) return "RestaurantFoodSupply.ReceiveFarmProduceLot: no id registry.";

            return stock.ReceiveLot(new RestaurantFoodLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FoodName = ProduceMaterialId,
                Units = units,
                AcquiredDayIndex = dayIndex,
                SupplierBusinessId = produceLot.FarmBusinessId,
                SourceLotId = produceLot.LotId,
                SupplierNote = $"{produceLot.ProductKind} from {produceLot.FarmBusinessName}, produced day {produceLot.ProducedDayIndex}",
                IsBootstrapEndowment = false,
            }, diag);
        }

        /// <summary>
        /// Takes milk units from a dairy milk lot into the pantry. Cow and
        /// milking provenance ride along; milk past its fresh days is refused
        /// (Canon §9.1: spoilage is a real outcome).
        /// </summary>
        public static string ReceiveMilkLot(
            RestaurantFoodStock stock,
            MilkLot milkLot,
            int units,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "RestaurantFoodSupply.ReceiveMilkLot: no pantry.";
            if (milkLot == null || units <= 0)
                return "RestaurantFoodSupply.ReceiveMilkLot: no milk lot offered — milk is not conjured.";
            if (milkLot.IsSpoiledFully(dayIndex))
                return $"RestaurantFoodSupply.ReceiveMilkLot: milk lot {milkLot.LotId} is past its fresh days — spoiled milk never enters the pantry.";
            if (idRegistry == null) return "RestaurantFoodSupply.ReceiveMilkLot: no id registry.";

            return stock.ReceiveLot(new RestaurantFoodLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FoodName = DairyMaterialId,
                Units = units,
                AcquiredDayIndex = dayIndex,
                SupplierBusinessId = milkLot.FarmId,
                SourceLotId = milkLot.LotId.ToString(),
                SupplierNote = $"milk from cow {milkLot.CowId}, milked day {milkLot.ProducedDayIndex} by {milkLot.MilkedBy}",
                IsBootstrapEndowment = false,
            }, diag);
        }

        /// <summary>
        /// Takes butter units from a dairy churn batch into the pantry. The
        /// churn batch id and its input milk lots are the provenance.
        /// </summary>
        public static string ReceiveButterBatch(
            RestaurantFoodStock stock,
            DairyProcessingBatch butterBatch,
            int units,
            string dairyBusinessId,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "RestaurantFoodSupply.ReceiveButterBatch: no pantry.";
            if (butterBatch == null || units <= 0)
                return "RestaurantFoodSupply.ReceiveButterBatch: no butter batch offered — butter is not conjured.";
            if (string.IsNullOrWhiteSpace(dairyBusinessId))
                return "RestaurantFoodSupply.ReceiveButterBatch: the dairy business must be named — no orphan stock.";
            if (idRegistry == null) return "RestaurantFoodSupply.ReceiveButterBatch: no id registry.";

            return stock.ReceiveLot(new RestaurantFoodLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FoodName = DairyMaterialId,
                Units = units,
                AcquiredDayIndex = dayIndex,
                SupplierBusinessId = dairyBusinessId,
                SourceLotId = butterBatch.BatchId.ToString(),
                SupplierNote = $"churned butter (batch of {butterBatch.ProcessType}, worker {butterBatch.WorkerId}, " +
                    $"day {butterBatch.StartDayIndex}); milk lots: {string.Join(", ", butterBatch.InputLotIds ?? new List<EntityId>())}",
                IsBootstrapEndowment = false,
            }, diag);
        }

        /// <summary>
        /// Receives an import order's arrival into the pantry as a named lot.
        /// The caller moves real lots; this only records custody with provenance.
        /// </summary>
        public static string ReceiveImportArrival(
            RestaurantFoodStock stock,
            string foodName,
            int units,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "RestaurantFoodSupply.ReceiveImportArrival: no pantry.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "RestaurantFoodSupply.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (idRegistry == null) return "RestaurantFoodSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new RestaurantFoodLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FoodName = foodName ?? string.Empty,
                Units = units,
                AcquiredDayIndex = dayIndex,
                ImportOrderId = importOrderId,
                OriginName = originName ?? string.Empty,
                IsBootstrapEndowment = false,
            }, diag);
        }
    }
}
