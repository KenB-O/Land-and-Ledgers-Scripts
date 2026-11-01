using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// W2C: one pantry lot with full upstream provenance. Every pound of
    /// meat, every loaf, every produce unit, every dairy unit must trace to
    /// a real lot from a real supplier — the butcher's carcass chain, the
    /// bakery's bread lots, a farm's produce lots, the dairy's milk/butter
    /// with cow provenance, a declared import order from a named off-map
    /// origin, or a named local supply relationship — or to the explicit
    /// one-time bootstrap endowment. No orphan lots, no synthetic stock
    /// (upstream-provenance doctrine). Same contract as
    /// RestaurantFoodLot (W2B); a separate type so boarding-house pantry
    /// stock can never be confused with eating-house stock.
    /// </summary>
    [Serializable]
    public sealed class BoardingHouseFoodLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string FoodName = string.Empty; // boardinghouse-meat / -bread / -produce / -dairy
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

        public BoardingHouseFoodLot() { }

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

    /// <summary>W2C: one dispense of pantry stock into a board meal — the audit trail of what went into the pot.</summary>
    [Serializable]
    public sealed class BoardingHouseFoodDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string FoodName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public BoardingHouseFoodDispenseLine() { }
    }

    /// <summary>
    /// W2C: the boarding-house pantry — lots in, dispensed units out, FIFO
    /// (oldest lot first) so stock ages honestly. Refusals are loud; empty
    /// shelves stay empty.
    /// </summary>
    public sealed class BoardingHouseFoodStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<BoardingHouseFoodLot> lots = new List<BoardingHouseFoodLot>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BoardingHouseFoodLot> Lots => lots;

        /// <summary>Receives a lot with provenance. Returns the refusal, or null.</summary>
        public string ReceiveLot(BoardingHouseFoodLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null)
                return "BoardingHouseFoodStock.ReceiveLot: no lot offered — food is not conjured.";
            if (lot.LotId == EntityId.Invalid)
                return "BoardingHouseFoodStock.ReceiveLot: a lot needs an EntityId — anonymous stock is refused.";
            if (string.IsNullOrWhiteSpace(lot.FoodName))
                return "BoardingHouseFoodStock.ReceiveLot: a lot needs a food name.";
            if (lot.Units <= 0)
                return "BoardingHouseFoodStock.ReceiveLot: a lot needs positive units.";
            if (!lot.IsBootstrapEndowment
                && string.IsNullOrWhiteSpace(lot.SupplierBusinessId)
                && string.IsNullOrWhiteSpace(lot.ImportOrderId)
                && string.IsNullOrWhiteSpace(lot.OriginName))
                return "BoardingHouseFoodStock.ReceiveLot: no supplier, import order, or origin — orphan stock is refused.";

            lots.Add(lot);
            diag.Add($"BoardingHouseFoodStock: received {lot.Units} × {lot.FoodName} (lot {lot.LotId}) — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses up to the requested units, oldest lots first, recording
        /// the dispense lines with provenance. Returns the lines actually
        /// dispensed; a shortfall returns fewer lines, never invented units.
        /// </summary>
        public List<BoardingHouseFoodDispenseLine> TryDispenseUnits(string foodName, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<BoardingHouseFoodDispenseLine>();
            if (string.IsNullOrWhiteSpace(foodName) || units <= 0) return lines;

            lots.Sort((a, b) =>
            {
                int day = a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex);
                return day != 0 ? day : a.LotId.ToString().CompareTo(b.LotId.ToString());
            });

            int remaining = units;
            for (int i = 0; i < lots.Count && remaining > 0; i++)
            {
                BoardingHouseFoodLot lot = lots[i];
                if (lot == null || lot.Units <= 0) continue;
                if (!string.Equals(lot.FoodName, foodName, StringComparison.OrdinalIgnoreCase)) continue;
                int taken = Math.Min(lot.Units, remaining);
                lot.Units -= taken;
                remaining -= taken;
                lines.Add(new BoardingHouseFoodDispenseLine
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
                diag.Add($"BoardingHouseFoodStock: shortfall dispensing {foodName} — {dispensed}/{units} unit(s) (day {dayIndex}). The pantry stays honest.");
            }
            return lines;
        }

        public int UnitsOnHand(string foodName)
        {
            if (string.IsNullOrWhiteSpace(foodName)) return 0;
            int total = 0;
            for (int i = 0; i < lots.Count; i++)
            {
                BoardingHouseFoodLot lot = lots[i];
                if (lot != null && string.Equals(lot.FoodName, foodName, StringComparison.OrdinalIgnoreCase))
                    total += Math.Max(0, lot.Units);
            }
            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingHouseFoodStockSaveDto
        {
            public List<BoardingHouseFoodLot> Lots = new List<BoardingHouseFoodLot>();
        }

        public BoardingHouseFoodStockSaveDto CaptureSaveDto()
        {
            var dto = new BoardingHouseFoodStockSaveDto();
            foreach (BoardingHouseFoodLot lot in lots)
            {
                if (lot == null || lot.Units <= 0) continue;
                dto.Lots.Add(lot);
            }
            return dto;
        }

        public void LoadFromSaveDto(BoardingHouseFoodStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            foreach (BoardingHouseFoodLot lot in dto.Lots)
            {
                if (lot == null || lot.LotId == EntityId.Invalid || lot.Units <= 0) continue;
                lots.Add(lot);
            }
        }
        #endregion
    }

    /// <summary>
    /// W2C: the explicit one-time opening pantry endowment. A boarding house
    /// feeds roughly twice as many mouths as an eating house per boarder
    /// week, so its opening stock is larger than the restaurant's (W2B).
    /// These lots are flagged; they never auto-replenish.
    /// </summary>
    public static class BoardingHouseFoodBootstrap
    {
        /// <summary>TUNING: opening meat (lb) in the pantry.</summary>
        public const int BootstrapMeatUnits = 60;
        /// <summary>TUNING: opening bread loaves in the pantry.</summary>
        public const int BootstrapBreadUnits = 40;
        /// <summary>TUNING: opening produce units in the pantry.</summary>
        public const int BootstrapProduceUnits = 40;
        /// <summary>TUNING: opening dairy units in the pantry.</summary>
        public const int BootstrapDairyUnits = 20;

        public static void ApplyBootstrapEndowment(
            BoardingHouseFoodStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("BoardingHouseFoodBootstrap: no pantry — endowment not applied.");
                return;
            }
            if (idRegistry == null)
            {
                diagnostics.Add("BoardingHouseFoodBootstrap: no id registry — endowment not applied.");
                return;
            }

            var endowments = new (string foodName, int units)[]
            {
                (BoardingHouseFoodSupply.MeatMaterialId, BootstrapMeatUnits),
                (BoardingHouseFoodSupply.BreadMaterialId, BootstrapBreadUnits),
                (BoardingHouseFoodSupply.ProduceMaterialId, BootstrapProduceUnits),
                (BoardingHouseFoodSupply.DairyMaterialId, BootstrapDairyUnits),
            };

            foreach (var (foodName, units) in endowments)
            {
                string rejection = stock.ReceiveLot(new BoardingHouseFoodLot
                {
                    LotId = idRegistry.Allocate(EntityKind.Lot),
                    FoodName = foodName,
                    Units = units,
                    AcquiredDayIndex = dayIndex,
                    IsBootstrapEndowment = true,
                    SupplierNote = "Opening boarding-house stocking (one-time; reorder via real suppliers and import orders only).",
                }, diagnostics);
                if (rejection != null)
                {
                    diagnostics.Add($"BoardingHouseFoodBootstrap: {rejection}");
                    return;
                }
            }

            diagnostics.Add("BoardingHouseFoodBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: these lots are explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// W2C: the pantry's supply link. Four honest paths into the pantry,
    /// each preserving the supplier's own provenance —
    /// <list type="bullet">
    /// <item>the butcher's <see cref="ButcherLot"/> meat (carcass chain: source animal, produced day, condition at receipt);</item>
    /// <item>the bakery's <see cref="BakeryBreadLot"/> loaves (baked day, flour provenance baked in);</item>
    /// <item>farm <see cref="FarmProduceLot"/> produce (farm business id/name, produced day);</item>
    /// <item>dairy <see cref="MilkLot"/> milk (cow provenance, milking record).</item>
    /// </list>
    /// For towns missing a supplier, the honest fallback is a DECLARED
    /// off-map trade link (EQU-1 precedent: named origin, real distance and
    /// transit days). The shop reorders through <see cref="ImportService"/>.
    /// Separate material ids from the restaurant's so the two pantries can
    /// never be confused.
    /// </summary>
    public static class BoardingHouseFoodSupply
    {
        public const string MeatMaterialId = "boardinghouse-meat";
        public const string BreadMaterialId = "boardinghouse-bread";
        public const string ProduceMaterialId = "boardinghouse-produce";
        public const string DairyMaterialId = "boardinghouse-dairy";

        /// <summary>
        /// Registers the boarding house's ingredients as importable materials
        /// (EQU-1 extension path). Prices/transit are calibration (Canon Part XV).
        /// </summary>
        public static void EnsureBoardingHouseImportables(List<string> diag)
        {
            Register(MeatMaterialId, "Meat (by the pound), boarding house", 250, 14, 12, diag);
            Register(BreadMaterialId, "Bread loaves, boarding house", 250, 14, 8, diag);
            Register(ProduceMaterialId, "Farm produce, boarding house", 250, 14, 5, diag);
            Register(DairyMaterialId, "Dairy (milk/butter), boarding house", 250, 14, 10, diag);
        }

        private static void Register(string materialId, string displayName, int distanceMiles, int transitDays, int pricePerUnitCents, List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                materialId, displayName, "Off-map provision wholesaler, via railhead",
                distanceMiles, transitDays, pricePerUnitCents));
            if (problem != null && diag != null)
            {
                diag.Add($"BoardingHouseFoodSupply: '{materialId}' import registration: {problem}");
            }
        }

        /// <summary>
        /// Takes lbs of meat from a butcher's lot into the pantry. The
        /// carcass chain (source animal, produced day, condition at receipt)
        /// is preserved on the pantry lot — the caller takes the lbs on the
        /// butcher's side; this records custody with provenance.
        /// </summary>
        public static string ReceiveButcherLot(
            BoardingHouseFoodStock stock,
            ButcherLot butcherLot,
            int lbs,
            string butcherBusinessId,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "BoardingHouseFoodSupply.ReceiveButcherLot: no pantry.";
            if (butcherLot == null || lbs <= 0)
                return "BoardingHouseFoodSupply.ReceiveButcherLot: no butcher lot offered — meat is not conjured.";
            if (string.IsNullOrWhiteSpace(butcherBusinessId))
                return "BoardingHouseFoodSupply.ReceiveButcherLot: the butcher business must be named — no orphan stock.";
            if (idRegistry == null) return "BoardingHouseFoodSupply.ReceiveButcherLot: no id registry.";

            return stock.ReceiveLot(new BoardingHouseFoodLot
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
            BoardingHouseFoodStock stock,
            BakeryBreadLot breadLot,
            int loaves,
            string bakeryBusinessId,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "BoardingHouseFoodSupply.ReceiveBakeryBreadLot: no pantry.";
            if (breadLot == null || loaves <= 0)
                return "BoardingHouseFoodSupply.ReceiveBakeryBreadLot: no bread lot offered — bread is not conjured.";
            if (!breadLot.CanSell)
                return $"BoardingHouseFoodSupply.ReceiveBakeryBreadLot: bread lot {breadLot.LotId} is {breadLot.Condition} — stale bread never enters the pantry.";
            if (string.IsNullOrWhiteSpace(bakeryBusinessId))
                return "BoardingHouseFoodSupply.ReceiveBakeryBreadLot: the bakery business must be named — no orphan stock.";
            if (idRegistry == null) return "BoardingHouseFoodSupply.ReceiveBakeryBreadLot: no id registry.";

            return stock.ReceiveLot(new BoardingHouseFoodLot
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
        /// Takes produce units from a farm produce lot into the pantry.
        /// Farm business id/name and produced day ride along.
        /// </summary>
        public static string ReceiveFarmProduceLot(
            BoardingHouseFoodStock stock,
            FarmProduceLot produceLot,
            int units,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "BoardingHouseFoodSupply.ReceiveFarmProduceLot: no pantry.";
            if (produceLot == null || units <= 0)
                return "BoardingHouseFoodSupply.ReceiveFarmProduceLot: no produce lot offered — produce is not conjured.";
            if (idRegistry == null) return "BoardingHouseFoodSupply.ReceiveFarmProduceLot: no id registry.";

            return stock.ReceiveLot(new BoardingHouseFoodLot
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
        /// milking provenance ride along; milk past its fresh days is
        /// refused (Canon §9.1: spoilage is a real outcome).
        /// </summary>
        public static string ReceiveMilkLot(
            BoardingHouseFoodStock stock,
            MilkLot milkLot,
            int units,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "BoardingHouseFoodSupply.ReceiveMilkLot: no pantry.";
            if (milkLot == null || units <= 0)
                return "BoardingHouseFoodSupply.ReceiveMilkLot: no milk lot offered — milk is not conjured.";
            if (milkLot.IsSpoiledFully(dayIndex))
                return $"BoardingHouseFoodSupply.ReceiveMilkLot: milk lot {milkLot.LotId} is past its fresh days — spoiled milk never enters the pantry.";
            if (idRegistry == null) return "BoardingHouseFoodSupply.ReceiveMilkLot: no id registry.";

            return stock.ReceiveLot(new BoardingHouseFoodLot
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
        /// Receives an import order's arrival into the pantry as a named lot.
        /// The caller moves real lots; this only records custody with
        /// provenance (declared off-map trade link, EQU-1).
        /// </summary>
        public static string ReceiveImportArrival(
            BoardingHouseFoodStock stock,
            string foodName,
            int units,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "BoardingHouseFoodSupply.ReceiveImportArrival: no pantry.";
            if (string.IsNullOrWhiteSpace(foodName) || units <= 0)
                return "BoardingHouseFoodSupply.ReceiveImportArrival: nothing arrived — food is not conjured.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "BoardingHouseFoodSupply.ReceiveImportArrival: the import order must be named — no orphan stock.";
            if (string.IsNullOrWhiteSpace(originName))
                return "BoardingHouseFoodSupply.ReceiveImportArrival: the off-map origin must be named (declared trade link).";
            if (idRegistry == null) return "BoardingHouseFoodSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new BoardingHouseFoodLot
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
