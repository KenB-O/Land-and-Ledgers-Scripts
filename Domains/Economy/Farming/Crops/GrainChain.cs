using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// CRP-3: the grain middleman capabilities. Canon R6 (Feed, Grain &amp;
    /// Related Merchant Forms) is explicit: feed retail, grain buying, storage,
    /// and milling are related but SEPARABLE commercial capabilities, not one
    /// atomic Feed &amp; Grain business class. "BUSINESS FORM FOLLOWS ACTUAL
    /// FUNCTION" (CANON LOCK). These register as BIZ-2 capabilities; the
    /// businesses holding them are created through the BIZ-1 workflow.
    /// </summary>
    public static class GrainCapabilities
    {
        public const string GrainDealingCapabilityId = "grain-dealing";
        public const string GrainStorageCapabilityId = "grain-storage";
        public const string MillingCapabilityId = "milling";
        public const string FeedRetailCapabilityId = "feed-retail";

        /// <summary>Capabilities are data (Tech X §3.1); the registry stays the single authority.</summary>
        public static void RegisterAll(BusinessCapabilityRegistry registry, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (registry == null)
            {
                diagnostics.Add("No capability registry — grain capabilities not registered.");
                return;
            }

            // Canon R6 §1.3: producer-facing aggregation; bears working-capital
            // and market exposure; distinct from feed retail.
            registry.Register(new BusinessCapability(
                GrainDealingCapabilityId,
                "Grain Dealing (Aggregation)",
                taskMethodIds: new[] { "buy_grain_farmgate", "sell_grain_bulk" },
                spaceRequirement: new PremisesRequirement(yardStorage: true)),
                diagnostics);

            // Canon R6: storage/handling may coexist with dealing but is not
            // synonymous with it.
            registry.Register(new BusinessCapability(
                GrainStorageCapabilityId,
                "Grain Storage (Elevator/Warehouse)",
                taskMethodIds: new[] { "receive_grain_storage", "release_grain_storage" },
                spaceRequirement: new PremisesRequirement(yardStorage: true, minAreaSqFt: 400)),
                diagnostics);

            // Canon R6: a mill may sell flour/feed through an attached or remote
            // commercial outlet.
            registry.Register(new BusinessCapability(
                MillingCapabilityId,
                "Milling (Grain → Flour/Feed/Bran)",
                taskMethodIds: new[] { CropChain.MillGrainTaskId },
                spaceRequirement: new PremisesRequirement(yardStorage: true, minAreaSqFt: 600)),
                diagnostics);

            // Canon R6 §1.2: feed commerce is a valid independent business role;
            // a general merchant may also carry feed if stock policy, storage,
            // and suppliers support it.
            registry.Register(new BusinessCapability(
                FeedRetailCapabilityId,
                "Feed Retail",
                taskMethodIds: new[] { "sell_feed_retail", "receive_feed_stock" },
                spaceRequirement: new PremisesRequirement(yardStorage: true)),
                diagnostics);
        }
    }

    /// <summary>
    /// CRP-3: a mill product lot with provenance. Milling splits one grain lot
    /// into flour, feed (mill feed), and bran — each a real lot, never an
    /// abstract conversion (Canon R6: milling is a real process with real
    /// outputs).
    /// </summary>
    [Serializable]
    public sealed class MillLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string ProductKind = string.Empty; // "flour", "feed", "bran"
        public int QuantityUnits;
        public string SourceGrainLotId = string.Empty; // upstream provenance
        public string MillerBusinessId = string.Empty;
        public int MilledDayIndex;
        public EntityId MilledBy = EntityId.Invalid; // worker (labor provenance)

        public MillLot() { }
    }

    /// <summary>
    /// CRP-3: one milling batch record — grain in, flour/feed/bran out, with
    /// the worker and the task named. Ratios are calibration (Canon Part XV).
    /// </summary>
    [Serializable]
    public sealed class MillingBatch
    {
        public EntityId BatchId = EntityId.Invalid;
        public string GrainLotId = string.Empty;
        public int GrainUnitsIn;
        public int FlourUnits;
        public int FeedUnits;
        public int BranUnits;
        public EntityId WorkerId = EntityId.Invalid;
        public int DayIndex;
        public string MillerBusinessId = string.Empty;
    }

    /// <summary>
    /// CRP-3: the grain dealer — producer-facing aggregation (Canon R6 §1.3).
    /// Buys grain at the farm gate, bears working-capital and market exposure,
    /// and resells into the larger market (miller, elevator, shipment). The
    /// dealer cannot buy what its working capital cannot cover, and cannot sell
    /// what it does not hold (no phantom grain — MR-P001 doctrine).
    /// </summary>
    public sealed class GrainDealer
    {
        public string DealerBusinessId = string.Empty;
        public int WorkingCapitalCents { get; private set; }

        private readonly List<CropLot> inventory = new List<CropLot>();

        public GrainDealer() { }

        public GrainDealer(string dealerBusinessId, int workingCapitalCents)
        {
            DealerBusinessId = dealerBusinessId ?? string.Empty;
            WorkingCapitalCents = Math.Max(0, workingCapitalCents);
        }

        public IReadOnlyList<CropLot> Inventory => inventory;

        public int InventoryUnits
        {
            get
            {
                int total = 0;
                foreach (var lot in inventory) total += Math.Max(0, lot.QuantityUnits);
                return total;
            }
        }

        /// <summary>
        /// Buys a grain lot at the farm gate. The farmer's ledger records
        /// SaleProceeds with the lot as reference (Canon XIII §13.2); the
        /// dealer's working capital drops by the purchase cost.
        /// </summary>
        public string BuyGrain(
            CropLot lot, int pricePerUnitCents, int dayIndex,
            HouseholdLedger farmerLedger, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (lot == null || lot.QuantityUnits <= 0)
            {
                return "GrainDealer.BuyGrain: no grain offered.";
            }
            if (lot.ProductKind != "grain")
            {
                return $"GrainDealer.BuyGrain: lot {lot.LotId} is {lot.ProductKind} — the dealer buys threshed grain, not sheaves.";
            }
            if (farmerLedger == null)
            {
                return "GrainDealer.BuyGrain: no farmer ledger — sale proceeds require provenance (Canon 13.2).";
            }

            int costCents = lot.QuantityUnits * Math.Max(0, pricePerUnitCents);
            if (costCents > WorkingCapitalCents)
            {
                return $"GrainDealer.BuyGrain: {lot.QuantityUnits} units at {pricePerUnitCents}c = {costCents}c exceeds working capital {WorkingCapitalCents}c — the dealer bears real capital exposure (Canon R6 §1.3).";
            }

            WorkingCapitalCents -= costCents;
            inventory.Add(lot);

            string problem = farmerLedger.RecordInflow(
                dayIndex, costCents,
                HouseholdIncomeSource.SaleProceeds,
                lot.LotId.ToString(),
                $"grain sale: {lot.QuantityUnits} units of {lot.Crop} to dealer {DealerBusinessId}",
                DealerBusinessId);
            if (problem != null) diagnostics.Add("GrainDealer.BuyGrain ledger note: " + problem);

            diagnostics.Add($"GrainDealer: bought {lot.QuantityUnits} grain units from farm {lot.FarmId} for {costCents}c.");
            return null;
        }

        /// <summary>
        /// Sells grain from inventory to a miller (or other buyer). The buyer
        /// must pay; the dealer cannot sell grain it does not hold.
        /// T1A wart fix: the aggregate resale lot now takes a real HF-1 lot id
        /// from the caller's id registry instead of leaving LotId Invalid.
        /// </summary>
        public CropLot SellGrain(
            int requestedUnits, int pricePerUnitCents, int dayIndex,
            HouseholdLedger dealerLedger, List<string> diagnostics,
            EntityIdRegistry idRegistry = null)
        {
            diagnostics ??= new List<string>();
            if (requestedUnits <= 0)
            {
                diagnostics.Add("GrainDealer.SellGrain: no units requested.");
                return null;
            }

            int available = InventoryUnits;
            int sold = Math.Min(requestedUnits, available);
            if (sold <= 0)
            {
                diagnostics.Add($"GrainDealer: no grain in inventory — cannot sell what it does not hold (MR-P001).");
                return null;
            }

            // Take oldest first (simple FIFO).
            int remaining = sold;
            CropLot soldLot = null;
            var kept = new List<CropLot>();
            foreach (var lot in inventory)
            {
                if (remaining <= 0) { kept.Add(lot); continue; }
                int take = Math.Min(remaining, lot.QuantityUnits);
                lot.QuantityUnits -= take;
                remaining -= take;
                if (soldLot == null)
                {
                    soldLot = new CropLot
                    {
                        // T1A: aggregate resale lots take a real id when the caller
                        // provides the registry; without one the lot is still a real
                        // commercial lot but its id is honestly reported as unregistered.
                        LotId = idRegistry != null ? idRegistry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                        Crop = lot.Crop,
                        ProductKind = "grain",
                        FarmId = lot.FarmId,
                        FieldId = lot.FieldId,
                        SeedSource = lot.SeedSource,
                        StorageNote = lot.StorageNote,
                    };
                }
                soldLot.QuantityUnits += take;
                if (lot.QuantityUnits > 0) kept.Add(lot);
            }
            inventory.Clear();
            inventory.AddRange(kept);

            int proceedsCents = sold * Math.Max(0, pricePerUnitCents);
            WorkingCapitalCents += proceedsCents;
            if (dealerLedger != null && proceedsCents > 0)
            {
                string problem = dealerLedger.RecordInflow(
                    dayIndex, proceedsCents,
                    HouseholdIncomeSource.SaleProceeds,
                    soldLot.LotId.ToString(),
                    $"grain resale: {sold} units via dealer {DealerBusinessId}",
                    DealerBusinessId);
                if (problem != null) diagnostics.Add("GrainDealer.SellGrain ledger note: " + problem);
            }

            if (sold < requestedUnits)
            {
                diagnostics.Add($"GrainDealer: only {sold}/{requestedUnits} units available — partial fill (Canon §9.2 finite supply).");
            }
            return soldLot;
        }
    }

    /// <summary>
    /// CRP-3: grain storage — the elevator/warehouse capability (Canon R6: a
    /// storage and handling capability that may coexist with dealing but is not
    /// synonymous with it). Commodity-appropriate storage with real capacity;
    /// storage fees are real costs (Canon §7.4L: storage determines whether
    /// the producer can hold output and wait for a later market).
    /// </summary>
    public sealed class GrainElevator
    {
        /// <summary>Calibration: storage fee per unit per 30 days.</summary>
        public const int StorageFeePerUnitPer30DaysCents = 2;

        public string ElevatorBusinessId = string.Empty;
        public int CapacityUnits { get; private set; }

        private readonly List<CropLot> stored = new List<CropLot>();

        public GrainElevator() { }

        public GrainElevator(string elevatorBusinessId, int capacityUnits)
        {
            ElevatorBusinessId = elevatorBusinessId ?? string.Empty;
            CapacityUnits = Math.Max(0, capacityUnits);
        }

        public int StoredUnits
        {
            get
            {
                int total = 0;
                foreach (var lot in stored) total += Math.Max(0, lot.QuantityUnits);
                return total;
            }
        }

        /// <summary>Receives grain into storage. Refuses overflow — capacity is real.</summary>
        public string ReceiveGrain(CropLot lot, int dayIndex, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (lot == null || lot.QuantityUnits <= 0)
            {
                return "GrainElevator.ReceiveGrain: no grain received.";
            }
            if (StoredUnits + lot.QuantityUnits > CapacityUnits)
            {
                return $"GrainElevator: {lot.QuantityUnits} units would exceed capacity {CapacityUnits} (stored {StoredUnits}) — storage is finite (Canon §7.4L).";
            }
            stored.Add(lot);
            diagnostics.Add($"GrainElevator: stored {lot.QuantityUnits} grain units (lot {lot.LotId}).");
            return null;
        }

        /// <summary>Releases grain from storage; storage fees accrue to the storer.</summary>
        public (CropLot lot, int feeCents) ReleaseGrain(string lotId, int dayIndex, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            CropLot found = null;
            foreach (var lot in stored)
            {
                if (lot.LotId.ToString() == lotId) { found = lot; break; }
            }
            if (found == null)
            {
                diagnostics.Add($"GrainElevator: lot {lotId} not in storage — cannot release what is not held.");
                return (null, 0);
            }
            stored.Remove(found);
            int days = Math.Max(0, dayIndex - found.HarvestDayIndex);
            int feeCents = found.QuantityUnits * StorageFeePerUnitPer30DaysCents * Math.Max(1, days / 30);
            diagnostics.Add($"GrainElevator: released lot {lotId} ({found.QuantityUnits} units); storage fee {feeCents}c for {days} days.");
            return (found, feeCents);
        }
    }

    /// <summary>
    /// CRP-3: the miller. Milling turns grain into flour, feed (mill feed), and
    /// bran — each a real lot with provenance (Canon R6: the mill may sell
    /// flour/feed through an attached or remote commercial outlet). Milling is
    /// skilled task labor through the TTS-3 extension path, like dairy
    /// processing — never an abstract conversion.
    /// </summary>
    public sealed class Miller
    {
        /// <summary>TTS-3 extension-path skill: milling.</summary>
        public const string MillingSkillId = "milling";

        // Calibration: milling ratios per 10 grain units (Canon Part XV).
        public const int FlourUnitsPer10Grain = 7;
        public const int FeedUnitsPer10Grain = 2;
        public const int BranUnitsPer10Grain = 1;
        /// <summary>Calibration: minutes to mill 100 grain units at skill 1.</summary>
        public const int MillMinutesPer100Units = 60;

        public string MillerBusinessId = string.Empty;

        private readonly List<MillingBatch> batches = new List<MillingBatch>();

        // EQP-2: mill station (Tech X §3.5). Not persisted — re-established from
        // assets on load. Mills that never establish one keep legacy behavior.
        private WorkstationInstance millStation;
        private Func<string, WorkstationComponentView?> millStationFinder;

        public Miller() { }

        public Miller(string millerBusinessId)
        {
            MillerBusinessId = millerBusinessId ?? string.Empty;
        }

        /// <summary>
        /// EQP-2: establishes the mill station from actual components. Milling
        /// requires stones AND motive power (both hard) — the caller checks power
        /// separately via the definition's MotivePowerSatisfied (Tech X §3.6).
        /// </summary>
        public void EstablishMillStation(WorkstationInstance station, Func<string, WorkstationComponentView?> findComponent)
        {
            millStation = station;
            millStationFinder = findComponent;
        }

        /// <summary>
        /// EQP-2: null when the station is ready or not established (legacy);
        /// the reason when established but not ready.
        /// </summary>
        public string CheckMillStation(List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (millStation == null) return null;
            return millStation.EvaluateReady(WorkstationCatalog.GrainMillStation, millStationFinder, diagnostics);
        }

        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("Miller: no SkillService — milling skill not registered.");
                return;
            }
            if (skillService.GetSkill(MillingSkillId) == null)
            {
                string rejection;
                if (!skillService.RegisterSkill(
                    new SkillDefinition(MillingSkillId, "Milling",
                        "Grinding grain into flour, feed, and bran. Registered by the grain chain (TTS-3 extension path)."),
                    out rejection))
                {
                    diagnostics.Add("Miller: milling skill rejected: " + rejection);
                }
            }
        }

        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;
            var mill = new TaskDefinition(CropChain.MillGrainTaskId, "Mill grain", MillMinutesPer100Units);
            mill.SetRequiredSkill(MillingSkillId, new[] { "milling" });
            // NX-1A: milling requires the millstones-and-power workstation
            // (EQP-2 GrainMillStation). Stones + power are both hard
            // requirements (Tech X §3.5, §3.6); the gate also evaluates the
            // station's support requirements (Canon 5.2).
            mill.EquipmentClasses.Add(EquipmentRequirementCodes.Workstation("grain-mill-station"));
            authority.RegisterDefinition(mill, out _);
        }

        /// <summary>
        /// Mills a grain lot into flour + feed + bran. The caller assigns the
        /// mill-grain task through TaskAuthority; this records the outcome with
        /// full provenance. Returns the batch, or null with diagnostics.
        /// </summary>
        public MillingBatch MillGrain(
            EntityIdRegistry idRegistry,
            CropLot grainLot, EntityId workerId, int dayIndex, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (idRegistry == null) return null;
            if (grainLot == null || grainLot.QuantityUnits <= 0)
            {
                diagnostics.Add("Miller.MillGrain: no grain to mill — flour is not conjured.");
                return null;
            }
            if (grainLot.ProductKind != "grain")
            {
                diagnostics.Add($"Miller.MillGrain: lot {grainLot.LotId} is {grainLot.ProductKind}, not threshed grain.");
                return null;
            }

            // EQP-2: mill-station gate (Tech X §3.5, §3.9) — enforced only once
            // the miller establishes its station from actual components.
            string stationReason = CheckMillStation(diagnostics);
            if (stationReason != null) return null;

            var batch = new MillingBatch
            {
                BatchId = idRegistry.Allocate(EntityKind.Lot),
                GrainLotId = grainLot.LotId.ToString(),
                GrainUnitsIn = grainLot.QuantityUnits,
                WorkerId = workerId,
                DayIndex = dayIndex,
                MillerBusinessId = MillerBusinessId,
            };
            batch.FlourUnits = (grainLot.QuantityUnits * FlourUnitsPer10Grain) / 10;
            batch.FeedUnits = (grainLot.QuantityUnits * FeedUnitsPer10Grain) / 10;
            batch.BranUnits = (grainLot.QuantityUnits * BranUnitsPer10Grain) / 10;
            batches.Add(batch);

            grainLot.QuantityUnits = 0; // consumed into the batch

            diagnostics.Add($"Miller: milled {batch.GrainUnitsIn} grain units → " +
                $"{batch.FlourUnits} flour + {batch.FeedUnits} feed + {batch.BranUnits} bran.");
            return batch;
        }

        /// <summary>Materializes a mill product lot from a batch (flour, feed, or bran).</summary>
        public MillLot TakeProduct(
            EntityIdRegistry idRegistry,
            MillingBatch batch, string productKind, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (idRegistry == null || batch == null) return null;
            int units = productKind == "flour" ? batch.FlourUnits
                : productKind == "feed" ? batch.FeedUnits
                : productKind == "bran" ? batch.BranUnits : 0;
            if (units <= 0)
            {
                diagnostics.Add($"Miller.TakeProduct: batch {batch.BatchId} has no {productKind} remaining.");
                return null;
            }
            if (productKind == "flour") batch.FlourUnits = 0;
            else if (productKind == "feed") batch.FeedUnits = 0;
            else batch.BranUnits = 0;

            return new MillLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ProductKind = productKind,
                QuantityUnits = units,
                SourceGrainLotId = batch.GrainLotId,
                MillerBusinessId = MillerBusinessId,
                MilledDayIndex = batch.DayIndex,
                MilledBy = batch.WorkerId,
            };
        }
    }

    /// <summary>
    /// CRP-3: a feed merchant backed by the mill — the "middle manager" between
    /// the miller and the livestock farmer. Implements the FVS-4 IFeedSupplier
    /// contract so farms buy through the existing FeedMarket: the stock names
    /// its upstream (mill ← dealer ← farm), and orphan restocks are refused.
    /// </summary>
    [Serializable]
    public sealed class MillFeedSupplier : IFeedSupplier
    {
        public string SupplierBusinessId { get; private set; }
        public string SupplierName { get; private set; }
        public int FeedStockUnits { get; private set; }
        public int PricePerUnitCents { get; private set; }
        public string UpstreamSource { get; private set; } = string.Empty;

        public MillFeedSupplier() { }

        public MillFeedSupplier(string businessId, string name, int pricePerUnitCents)
        {
            SupplierBusinessId = businessId ?? string.Empty;
            SupplierName = name ?? string.Empty;
            PricePerUnitCents = Math.Max(0, pricePerUnitCents);
        }

        public string RestockFeed(int units, string upstreamSource, int dayIndex)
        {
            if (units <= 0) return "MillFeedSupplier: no units to stock.";
            if (string.IsNullOrWhiteSpace(upstreamSource))
            {
                return "MillFeedSupplier: feed must name its upstream source (mill ← dealer ← farm) — no orphan inputs.";
            }
            FeedStockUnits += units;
            UpstreamSource = upstreamSource;
            return null;
        }

        public int SellFeed(int requestedUnits, int dayIndex, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (requestedUnits <= 0)
            {
                diagnostics.Add("MillFeedSupplier: no units requested.");
                return -1;
            }
            int sold = Math.Min(requestedUnits, FeedStockUnits);
            if (sold <= 0)
            {
                diagnostics.Add($"MillFeedSupplier: {SupplierName} has no feed in stock — finite supply (Canon §9.2).");
                return -1;
            }
            FeedStockUnits -= sold;
            if (sold < requestedUnits)
            {
                diagnostics.Add($"MillFeedSupplier: only {sold}/{requestedUnits} units available — partial fill.");
            }
            return sold;
        }
    }

    /// <summary>
    /// CRP-3: the honest economics note. A farm with its own grain faces a real
    /// choice: feed the grain to its animals (internal use — no transaction,
    /// recorded as feed stock with source "own crop") or sell it (foregoing
    /// the feed value). The opportunity cost is real but it is NOT a fake
    /// transaction — no money moves when grain goes to the farm's own trough.
    /// Bought feed vs home-grown feed is therefore costed honestly: growing
    /// costs land + labor + time (CRP-2), buying costs cash + supplier exposure.
    /// </summary>
    public static class GrainChainEconomics
    {
        /// <summary>
        /// Feeds own grain directly to the farm's animals: internal use, no
        /// sale, no phantom revenue. Returns units added to feed stock.
        /// </summary>
        public static int FeedOwnGrain(
            FeedLoop feedLoop, CropLot grainLot, int units, int dayIndex, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (feedLoop == null) return 0;
            if (grainLot == null || grainLot.QuantityUnits <= 0 || units <= 0)
            {
                diagnostics.Add("GrainChainEconomics: no grain to feed.");
                return 0;
            }
            int fed = Math.Min(units, grainLot.QuantityUnits);
            grainLot.QuantityUnits -= fed;
            feedLoop.AddFeed(fed, $"own grain, lot {grainLot.LotId} (day {dayIndex}) — internal use, no sale");
            diagnostics.Add($"GrainChainEconomics: fed {fed} own-grain units to livestock (opportunity cost: foregone grain sale — no money moved).");
            return fed;
        }
    }
}
