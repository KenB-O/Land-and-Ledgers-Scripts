using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// W3B: one hotel pantry lot with full upstream provenance. Every pound
    /// of meat, every loaf, every produce unit, every dairy unit the hotel
    /// kitchen cooks must trace to a real lot from a real supplier — the
    /// butcher's carcass chain, the bakery's bread lots, a farm's produce
    /// lots, the dairy's milk with cow provenance, a declared import order
    /// from a named off-map origin, or the explicit one-time bootstrap
    /// endowment. No orphan lots, no synthetic stock (upstream-provenance
    /// doctrine). The exact parallel of BoardingHouseFoodLot (W2C); a
    /// separate type with separate material ids so the hotel pantry can
    /// never be confused with the boarding-house or eating-house pantries.
    /// </summary>
    [Serializable]
    public sealed class HotelFoodLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string FoodName = string.Empty; // hotel-meat / -bread / -produce / -dairy
        public int Units; // lb of meat, loaves, produce units, dairy units
        public int AcquiredDayIndex;

        /// <summary>The business that supplied this lot (butcher/bakery/farm/dairy/import).</summary>
        public string SupplierBusinessId = string.Empty;
        /// <summary>The supplier-side lot this came from, where one exists.</summary>
        public string SourceLotId = string.Empty;
        /// <summary>Off-map import order id (EQU-1 declared trade link).</summary>
        public string ImportOrderId = string.Empty;
        /// <summary>Named off-map origin for imported lots.</summary>
        public string OriginName = string.Empty;
        /// <summary>Human-readable provenance carried from the supplier.</summary>
        public string SupplierNote = string.Empty;

        /// <summary>One-time opening endowment. Explicitly marked, never auto-replenished.</summary>
        public bool IsBootstrapEndowment;

        public HotelFoodLot() { }

        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
                return $"BOOTSTRAP endowment (one-time, day {AcquiredDayIndex})";
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(SupplierBusinessId)) parts.Add($"supplier {SupplierBusinessId}");
            if (!string.IsNullOrWhiteSpace(SourceLotId)) parts.Add($"source lot {SourceLotId}");
            if (!string.IsNullOrWhiteSpace(ImportOrderId)) parts.Add($"import order {ImportOrderId}");
            if (!string.IsNullOrWhiteSpace(OriginName)) parts.Add($"origin {OriginName}");
            if (!string.IsNullOrWhiteSpace(SupplierNote)) parts.Add(SupplierNote);
            if (parts.Count == 0) return "NO PROVENANCE";
            return string.Join(" | ", parts.ToArray());
        }
    }

    /// <summary>W3B: one dispense of hotel pantry stock into a dining-room meal — the audit trail of what went into the pot.</summary>
    [Serializable]
    public sealed class HotelFoodDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string FoodName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public HotelFoodDispenseLine() { }
    }

    /// <summary>
    /// W3B: the hotel pantry — lots in, dispensed units out, FIFO (oldest
    /// lot first) so stock ages honestly. Refusals are loud; empty shelves
    /// stay empty. The W2C pantry pattern, hotel-typed.
    /// </summary>
    public sealed class HotelFoodStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<HotelFoodLot> lots = new List<HotelFoodLot>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HotelFoodLot> Lots => lots;

        /// <summary>Receives a lot with provenance. Returns the refusal, or null.</summary>
        public string ReceiveLot(HotelFoodLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null)
                return "HotelFoodStock.ReceiveLot: no lot offered — food is not conjured.";
            if (lot.LotId == EntityId.Invalid)
                return "HotelFoodStock.ReceiveLot: a lot needs an EntityId — anonymous stock is refused.";
            if (string.IsNullOrWhiteSpace(lot.FoodName))
                return "HotelFoodStock.ReceiveLot: a lot needs a food name.";
            if (lot.Units <= 0)
                return "HotelFoodStock.ReceiveLot: a lot needs positive units.";
            if (!lot.IsBootstrapEndowment
                && string.IsNullOrWhiteSpace(lot.SupplierBusinessId)
                && string.IsNullOrWhiteSpace(lot.ImportOrderId)
                && string.IsNullOrWhiteSpace(lot.OriginName))
                return "HotelFoodStock.ReceiveLot: no supplier, import order, or origin — orphan stock is refused.";

            lots.Add(lot);
            diag.Add($"HotelFoodStock: received {lot.Units} × {lot.FoodName} (lot {lot.LotId}) — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses up to the requested units, oldest lots first, recording
        /// the dispense lines with provenance. Returns the lines actually
        /// dispensed; a shortfall returns fewer lines, never invented units.
        /// </summary>
        public List<HotelFoodDispenseLine> TryDispenseUnits(string foodName, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<HotelFoodDispenseLine>();
            if (string.IsNullOrWhiteSpace(foodName) || units <= 0) return lines;

            lots.Sort((a, b) =>
            {
                int day = a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex);
                return day != 0 ? day : a.LotId.ToString().CompareTo(b.LotId.ToString());
            });

            int remaining = units;
            for (int i = 0; i < lots.Count && remaining > 0; i++)
            {
                HotelFoodLot lot = lots[i];
                if (lot == null || lot.Units <= 0) continue;
                if (!string.Equals(lot.FoodName, foodName, StringComparison.OrdinalIgnoreCase)) continue;
                int taken = Math.Min(lot.Units, remaining);
                lot.Units -= taken;
                remaining -= taken;
                lines.Add(new HotelFoodDispenseLine
                {
                    LotId = lot.LotId,
                    FoodName = foodName,
                    UnitsTaken = taken,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            lots.RemoveAll(l => l == null || l.Units <= 0);

            int dispensed = units - remaining;
            if (dispensed < units)
            {
                diag.Add($"HotelFoodStock: shortfall dispensing {foodName} — {dispensed}/{units} unit(s) (day {dayIndex}). The pantry stays honest.");
            }
            return lines;
        }

        public int UnitsOnHand(string foodName)
        {
            if (string.IsNullOrWhiteSpace(foodName)) return 0;
            int total = 0;
            for (int i = 0; i < lots.Count; i++)
            {
                HotelFoodLot lot = lots[i];
                if (lot != null && string.Equals(lot.FoodName, foodName, StringComparison.OrdinalIgnoreCase))
                    total += Math.Max(0, lot.Units);
            }
            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelFoodStockSaveDto
        {
            public List<HotelFoodLot> Lots = new List<HotelFoodLot>();
        }

        public HotelFoodStockSaveDto CaptureSaveDto()
        {
            var dto = new HotelFoodStockSaveDto();
            foreach (HotelFoodLot lot in lots)
            {
                if (lot == null || lot.Units <= 0) continue;
                dto.Lots.Add(lot);
            }
            return dto;
        }

        public void LoadFromSaveDto(HotelFoodStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            foreach (HotelFoodLot lot in dto.Lots)
            {
                if (lot == null || lot.LotId == EntityId.Invalid || lot.Units <= 0) continue;
                lots.Add(lot);
            }
        }
        #endregion
    }

    /// <summary>
    /// W3B: the explicit one-time opening pantry endowment for the hotel
    /// dining room. Smaller than the boarding house's (W2C): a hotel feeds
    /// transient guests, not a full house of boarders. Flagged; never
    /// auto-replenished.
    /// </summary>
    public static class HotelFoodBootstrap
    {
        /// <summary>TUNING: opening meat (lb) in the hotel pantry.</summary>
        public const int BootstrapMeatUnits = 40;
        /// <summary>TUNING: opening bread loaves in the hotel pantry.</summary>
        public const int BootstrapBreadUnits = 30;
        /// <summary>TUNING: opening produce units in the hotel pantry.</summary>
        public const int BootstrapProduceUnits = 30;
        /// <summary>TUNING: opening dairy units in the hotel pantry.</summary>
        public const int BootstrapDairyUnits = 15;

        public static void ApplyBootstrapEndowment(
            HotelFoodStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("HotelFoodBootstrap: no pantry — endowment not applied.");
                return;
            }
            if (idRegistry == null)
            {
                diagnostics.Add("HotelFoodBootstrap: no id registry — endowment not applied.");
                return;
            }

            var endowments = new (string foodName, int units)[]
            {
                (HotelFoodSupply.MeatMaterialId, BootstrapMeatUnits),
                (HotelFoodSupply.BreadMaterialId, BootstrapBreadUnits),
                (HotelFoodSupply.ProduceMaterialId, BootstrapProduceUnits),
                (HotelFoodSupply.DairyMaterialId, BootstrapDairyUnits),
            };

            foreach (var (foodName, units) in endowments)
            {
                string rejection = stock.ReceiveLot(new HotelFoodLot
                {
                    LotId = idRegistry.Allocate(EntityKind.Lot),
                    FoodName = foodName,
                    Units = units,
                    AcquiredDayIndex = dayIndex,
                    IsBootstrapEndowment = true,
                    SupplierNote = "Opening hotel pantry stocking (one-time; reorder via real suppliers and import orders only).",
                }, diagnostics);
                if (rejection != null)
                {
                    diagnostics.Add($"HotelFoodBootstrap: {rejection}");
                    return;
                }
            }

            diagnostics.Add("HotelFoodBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: these lots are explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// W3B: the hotel pantry's supply link — the four honest paths into the
    /// pantry, each preserving the supplier's own provenance, exactly
    /// parallel to <c>BoardingHouseFoodSupply</c> (W2C) but hotel-typed with
    /// hotel material ids:
    /// <list type="bullet">
    /// <item>the butcher's meat (carcass chain);</item>
    /// <item>the bakery's bread loaves (baked day, flour provenance);</item>
    /// <item>farm produce (farm business id/name, produced day);</item>
    /// <item>dairy milk (cow provenance, milking record).</item>
    /// </list>
    /// For towns missing a supplier, the honest fallback is a DECLARED
    /// off-map trade link (EQU-1 precedent). The hotel reorders through
    /// <see cref="ImportService"/>.
    /// </summary>
    public static class HotelFoodSupply
    {
        public const string MeatMaterialId = "hotel-meat";
        public const string BreadMaterialId = "hotel-bread";
        public const string ProduceMaterialId = "hotel-produce";
        public const string DairyMaterialId = "hotel-dairy";

        /// <summary>
        /// Registers the hotel dining room's ingredients as importable
        /// materials (EQU-1 extension path). Prices/transit are calibration
        /// (Canon Part XV).
        /// </summary>
        public static void EnsureHotelFoodImportables(List<string> diag)
        {
            Register(MeatMaterialId, "Meat (by the pound), hotel dining room", 250, 14, 250, diag);
            Register(BreadMaterialId, "Bread loaves, hotel dining room", 250, 14, 8, diag);
            Register(ProduceMaterialId, "Farm produce, hotel dining room", 250, 14, 5, diag);
            Register(DairyMaterialId, "Dairy (milk/butter), hotel dining room", 250, 14, 10, diag);
        }

        private static void Register(string materialId, string displayName, int distanceMiles, int transitDays, int pricePerUnitCents, List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                materialId, displayName, "Off-map provision wholesaler, via railhead",
                distanceMiles, transitDays, pricePerUnitCents));
            if (problem != null && diag != null)
            {
                diag.Add($"HotelFoodSupply: '{materialId}' import registration: {problem}");
            }
        }

        /// <summary>
        /// Takes lbs of meat from a butcher's lot into the hotel pantry. The
        /// carcass chain (source animal, produced day, condition at receipt)
        /// is preserved on the pantry lot — the caller takes the lbs on the
        /// butcher's side; this records custody with provenance.
        /// </summary>
        public static string ReceiveButcherLot(
            HotelFoodStock stock,
            ButcherLot butcherLot,
            int lbs,
            string butcherBusinessId,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "HotelFoodSupply.ReceiveButcherLot: no pantry.";
            if (butcherLot == null || lbs <= 0)
                return "HotelFoodSupply.ReceiveButcherLot: no butcher lot offered — meat is not conjured.";
            if (string.IsNullOrWhiteSpace(butcherBusinessId))
                return "HotelFoodSupply.ReceiveButcherLot: the butcher business must be named — no orphan stock.";
            if (idRegistry == null) return "HotelFoodSupply.ReceiveButcherLot: no id registry.";

            return stock.ReceiveLot(new HotelFoodLot
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
        /// Takes loaves from a bakery's bread lot into the hotel pantry. The
        /// bread lot's baked day and baked-in flour provenance ride along.
        /// </summary>
        public static string ReceiveBakeryBreadLot(
            HotelFoodStock stock,
            BakeryBreadLot breadLot,
            int loaves,
            string bakeryBusinessId,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "HotelFoodSupply.ReceiveBakeryBreadLot: no pantry.";
            if (breadLot == null || loaves <= 0)
                return "HotelFoodSupply.ReceiveBakeryBreadLot: no bread lot offered — bread is not conjured.";
            if (!breadLot.CanSell)
                return $"HotelFoodSupply.ReceiveBakeryBreadLot: bread lot {breadLot.LotId} is {breadLot.Condition} — stale bread never enters the pantry.";
            if (string.IsNullOrWhiteSpace(bakeryBusinessId))
                return "HotelFoodSupply.ReceiveBakeryBreadLot: the bakery business must be named — no orphan stock.";
            if (idRegistry == null) return "HotelFoodSupply.ReceiveBakeryBreadLot: no id registry.";

            return stock.ReceiveLot(new HotelFoodLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FoodName = BreadMaterialId,
                Units = loaves,
                AcquiredDayIndex = dayIndex,
                SupplierBusinessId = bakeryBusinessId,
                SourceLotId = breadLot.LotId.ToString(),
                SupplierNote = $"bread baked day {breadLot.BakedDayIndex} at condition {breadLot.Condition}; " +
                    "flour provenance: " + string.Join(" | ", breadLot.InputProvenance ?? new List<string>()),
                IsBootstrapEndowment = false,
            }, diag);
        }

        /// <summary>
        /// Takes produce units from a farm produce lot into the hotel
        /// pantry. Farm business id/name and produced day ride along.
        /// </summary>
        public static string ReceiveFarmProduceLot(
            HotelFoodStock stock,
            FarmProduceLot produceLot,
            int units,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "HotelFoodSupply.ReceiveFarmProduceLot: no pantry.";
            if (produceLot == null || units <= 0)
                return "HotelFoodSupply.ReceiveFarmProduceLot: no produce lot offered — produce is not conjured.";
            if (idRegistry == null) return "HotelFoodSupply.ReceiveFarmProduceLot: no id registry.";

            return stock.ReceiveLot(new HotelFoodLot
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
        /// Takes milk units from a dairy milk lot into the hotel pantry.
        /// Cow and milking provenance ride along; milk past its fresh days
        /// is refused (Canon §9.1: spoilage is a real outcome).
        /// </summary>
        public static string ReceiveMilkLot(
            HotelFoodStock stock,
            MilkLot milkLot,
            int units,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "HotelFoodSupply.ReceiveMilkLot: no pantry.";
            if (milkLot == null || units <= 0)
                return "HotelFoodSupply.ReceiveMilkLot: no milk lot offered — milk is not conjured.";
            if (milkLot.IsSpoiledFully(dayIndex))
                return $"HotelFoodSupply.ReceiveMilkLot: milk lot {milkLot.LotId} is past its fresh days — spoiled milk never enters the pantry.";
            if (idRegistry == null) return "HotelFoodSupply.ReceiveMilkLot: no id registry.";

            return stock.ReceiveLot(new HotelFoodLot
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
        /// Receives an import order's arrival into the hotel pantry as a
        /// named lot. The caller moves real lots; this only records custody
        /// with provenance (declared off-map trade link, EQU-1).
        /// </summary>
        public static string ReceiveImportArrival(
            HotelFoodStock stock,
            string foodName,
            int units,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "HotelFoodSupply.ReceiveImportArrival: no pantry.";
            if (string.IsNullOrWhiteSpace(foodName) || units <= 0)
                return "HotelFoodSupply.ReceiveImportArrival: nothing arrived — food is not conjured.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "HotelFoodSupply.ReceiveImportArrival: the import order must be named — no orphan stock.";
            if (string.IsNullOrWhiteSpace(originName))
                return "HotelFoodSupply.ReceiveImportArrival: the off-map origin must be named (declared trade link).";
            if (idRegistry == null) return "HotelFoodSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new HotelFoodLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FoodName = foodName,
                Units = units,
                AcquiredDayIndex = dayIndex,
                ImportOrderId = importOrderId,
                OriginName = originName,
                SupplierNote = $"import arrival {importOrderId} from {originName}, day {dayIndex}",
                IsBootstrapEndowment = false,
            }, diag);
        }
    }
}
