using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainMill
{
    /// <summary>
    /// W5A: the recorded outcome of one grist batch — grain in, flour/meal/
    /// feed/bran out, with the mode, the customer (toll), the toll taken,
    /// and the worker and day named. Money moves only through ledger
    /// authorities; the batch records the toll owed so they can post it.
    /// </summary>
    [Serializable]
    public sealed class GristBatchRecord
    {
        public EntityId BatchId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public GristBatchMode Mode;
        public CropKind Crop;
        public string GrainLotId = string.Empty; // exactly one source grain lot (W4A provenance rule)
        public int GrainUnitsIn;
        public string MainProductKind = string.Empty; // "flour", "meal", "feed"
        public int MainUnits;
        public int FeedUnits;
        public int BranUnits;
        public int TollCashCents;      // toll mode only: owed by the customer, posted by the ledger authority
        public int TollInKindUnits;    // toll mode only: flour/meal units the mill kept in kind
        public string CustomerId = string.Empty;   // toll mode only
        public string CustomerName = string.Empty; // toll mode only
        public EntityId WorkerId = EntityId.Invalid; // the miller (labor provenance)
        public int DayIndex;
        public string MillerBusinessId = string.Empty;

        public GristBatchRecord() { }
    }

    /// <summary>
    /// W5A: the return value of a grist batch — the batch record plus the
    /// product lots it materialized. Lots the mill owns go into its product
    /// stock inside the runtime; the customer's toll lots are returned here
    /// (and held in the runtime's toll outbox until the customer collects).
    /// </summary>
    public sealed class GristBatchResult
    {
        public GristBatchRecord Record;
    }

    /// <summary>
    /// W5A: customer-owned product lots from toll grinds, held by the mill
    /// until the customer collects them. Never mill inventory — custody.
    /// </summary>
    [Serializable]
    public sealed class TollProductCustody
    {
        public MillLot Lot;
        public string CustomerId = string.Empty;
        public string CustomerName = string.Empty;

        public TollProductCustody() { }
    }

    /// <summary>
    /// W5A: the mill's own product stock — CRP MillLots the mill OWNS
    /// (merchant batches, in-kind toll flour, purchased toll byproducts).
    /// Units dispense FIFO (oldest stock first) for buyers: the bakery
    /// takes flour lots via its own intake (BakeryFlourSupply.ReceiveMillLot
    /// preserves the farm <- dealer <- mill chain), feed merchants via
    /// RestockFeed with named upstream. Refusals are loud — shortfalls are
    /// never faked.
    /// </summary>
    public sealed class GrainMillProductStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<MillLot> lots = new List<MillLot>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<MillLot> Lots => lots;

        public string ReceiveLot(MillLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null) return "GrainMillProductStock: null lot refused — no product without a lot record.";
            if (string.IsNullOrWhiteSpace(lot.ProductKind))
                return "GrainMillProductStock: lot refused — product kind is required.";
            if (lot.QuantityUnits <= 0)
                return $"GrainMillProductStock: lot refused — '{lot.ProductKind}' needs a positive unit count.";
            if (lot.LotId == EntityId.Invalid || !lot.LotId.IsValid)
                return "GrainMillProductStock: lot refused — a product lot needs a real EntityId (HF-1).";
            if (string.IsNullOrWhiteSpace(lot.SourceGrainLotId))
                return $"GrainMillProductStock: lot refused — '{lot.ProductKind}' names no source grain lot. No orphan lots.";
            lots.Add(lot);
            diag.Add($"GrainMillProductStock: received {lot.QuantityUnits}u '{lot.ProductKind}' (lot {lot.LotId}) <- grain lot {lot.SourceGrainLotId}.");
            return null;
        }

        /// <summary>
        /// Dispenses units FIFO (oldest first) into a fresh buyer lot. The
        /// source chain is preserved (all source grain lots named). Returns
        /// null with a diagnostic on shortfall — no units are conjured.
        /// </summary>
        public MillLot TryDispenseProduct(
            EntityIdRegistry idRegistry, string productKind, int units, string millerBusinessId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("GrainMillProductStock: no id registry — dispense refused.");
                return null;
            }
            if (units <= 0)
            {
                diag.Add("GrainMillProductStock: no units requested.");
                return null;
            }

            int available = UnitsOnHand(productKind);
            if (available < units)
            {
                diag.Add($"GrainMillProductStock: only {available}u of '{productKind}' on hand, need {units}u — shortfall, no units conjured.");
                return null;
            }

            var ordered = new List<MillLot>();
            foreach (var lot in lots)
            {
                if (string.Equals(lot.ProductKind, productKind, StringComparison.OrdinalIgnoreCase))
                    ordered.Add(lot);
            }
            ordered.Sort((a, b) => a.MilledDayIndex.CompareTo(b.MilledDayIndex));

            var sources = new List<string>();
            int minDay = int.MaxValue;
            EntityId firstWorker = EntityId.Invalid;
            string firstSource = string.Empty;
            int remaining = units;
            foreach (var lot in ordered)
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, lot.QuantityUnits);
                lot.QuantityUnits -= take;
                remaining -= take;
                if (!sources.Contains(lot.SourceGrainLotId)) sources.Add(lot.SourceGrainLotId);
                if (lot.MilledDayIndex < minDay) minDay = lot.MilledDayIndex;
                if (!firstWorker.IsValid) firstWorker = lot.MilledBy;
                if (string.IsNullOrEmpty(firstSource)) firstSource = lot.SourceGrainLotId;
            }
            lots.RemoveAll(l => l.QuantityUnits <= 0);

            var buyerLot = new MillLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ProductKind = productKind,
                QuantityUnits = units,
                SourceGrainLotId = string.Join(";", sources.ToArray()),
                MillerBusinessId = millerBusinessId ?? string.Empty,
                MilledDayIndex = minDay == int.MaxValue ? 0 : minDay,
                MilledBy = firstWorker,
            };
            diag.Add($"GrainMillProductStock: dispensed {units}u '{productKind}' (lot {buyerLot.LotId}) <- grain lot(s) {buyerLot.SourceGrainLotId}.");
            return buyerLot;
        }

        /// <summary>Counts all mill-owned units of a product kind.</summary>
        public int UnitsOnHand(string productKind)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.ProductKind, productKind, StringComparison.OrdinalIgnoreCase))
                    total += Math.Max(0, lot.QuantityUnits);
            }
            return total;
        }

        /// <summary>W5A save contract: lives inside the owning stock class.</summary>
        [Serializable]
        public sealed class GrainMillProductStockSaveDto
        {
            public List<MillLot> Lots = new List<MillLot>();
        }

        public GrainMillProductStockSaveDto CaptureSaveDto()
        {
            var dto = new GrainMillProductStockSaveDto();
            foreach (var lot in lots)
            {
                dto.Lots.Add(new MillLot
                {
                    LotId = lot.LotId,
                    ProductKind = lot.ProductKind,
                    QuantityUnits = lot.QuantityUnits,
                    SourceGrainLotId = lot.SourceGrainLotId,
                    MillerBusinessId = lot.MillerBusinessId,
                    MilledDayIndex = lot.MilledDayIndex,
                    MilledBy = lot.MilledBy,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(GrainMillProductStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            foreach (var lot in dto.Lots)
            {
                if (lot == null) continue;
                lots.Add(lot);
            }
        }
    }

    /// <summary>
    /// W5A: the per-instance grain-mill runtime — the detailed layer behind
    /// a GrainMill business instance (BusinessType.GrainMill = 12). One
    /// instance per business.
    ///
    /// What this owns (the detailed layer):
    /// - grain intake with provenance validation: merchant grain (mill-owned
    ///   inventory) and toll grain (customer custody, never mill inventory)
    ///   as two books that never mix;
    /// - grist batches (wheat -> flour, corn -> meal, oats -> feed) in TOLL
    ///   and MERCHANT mode as policy data, with ratios from the conversion
    ///   table, full provenance, and the toll recorded for ledger posting;
    /// - bran/feed byproducts as real lots, routed to feed merchants /
    ///   livestock feed honestly (MillFeedSupplier.RestockFeed with named
    ///   upstream), never synthesized;
    /// - millstone wear and the dressing loop: dull/broken stones gate ALL
    ///   grinding until the dresser works (Tech X §3.9), following the NX-1
    ///   equipment-gate pattern for the workstation itself.
    ///
    /// Money moves only through ledger authorities, never here. This sits
    /// ALONGSIDE the CRP-3 grain chain (Miller owns the "mill-grain" task
    /// registration and the raw MillingBatch authority) — no rewrite, no
    /// rename, no renumber. The miller's flour lots flow to the W2A bakery
    /// through BakeryFlourSupply.ReceiveMillLot, preserving the farm <-
    /// dealer <- mill chain.
    /// </summary>
    public sealed class GrainMillShopRuntime
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly string businessInstanceId;
        private readonly EntityIdRegistry idRegistry;
        private readonly GrainMillGrainStock grainStock = new GrainMillGrainStock();
        private readonly GrainMillProductStock productStock = new GrainMillProductStock();
        private readonly List<TollProductCustody> tollOutbox = new List<TollProductCustody>();
        private readonly GrainMillStoneState stones;
        private readonly GristMillPolicy policy = new GristMillPolicy();
        private readonly List<GristBatchRecord> batchHistory = new List<GristBatchRecord>();
        private readonly List<GristMillPurchaseRecord> purchaseHistory = new List<GristMillPurchaseRecord>();
        private readonly List<GristMillSaleRecord> saleHistory = new List<GristMillSaleRecord>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public GrainMillGrainStock GrainStock => grainStock;
        public GrainMillProductStock ProductStock => productStock;
        public IReadOnlyList<TollProductCustody> TollOutbox => tollOutbox;
        public GrainMillStoneState Stones => stones;
        public GristMillPolicy Policy => policy;
        public IReadOnlyList<GristBatchRecord> BatchHistory => batchHistory;
        /// <summary>D2E: the mill's merchant grain purchases (named sellers, named prices).</summary>
        public IReadOnlyList<GristMillPurchaseRecord> PurchaseHistory => purchaseHistory;
        /// <summary>D2E: the mill's merchant product sales (named buyers, named prices).</summary>
        public IReadOnlyList<GristMillSaleRecord> SaleHistory => saleHistory;

        public GrainMillShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry, string stoneId = null)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.stones = new GrainMillStoneState(string.IsNullOrWhiteSpace(stoneId)
                ? this.businessInstanceId + "-stones" : stoneId);
        }

        /// <summary>
        /// D2E: the mill buys merchant grain from a NAMED seller at a named
        /// or policy-booked price. The purchase is recorded with full
        /// provenance; the cost is RETURNED for the ledger authority to
        /// post (money never moves here). -1 on refusal.
        /// </summary>
        public int BuyMerchantLot(CropLot lot, string sellerId, string sellerName, int dayIndex, List<string> diag, int? pricePerUnitCents = null)
        {
            return GristMillMerchantTrade.BuyMerchantLot(this, lot, sellerId, sellerName, dayIndex, diag ?? diagnostics, pricePerUnitCents);
        }

        /// <summary>
        /// D2E: the mill sells mill-owned product to a NAMED buyer at a
        /// named or policy-booked price — the flour/feed outlet channel
        /// (Canon R6 lock). Units dispense FIFO with the grain chain intact;
        /// the revenue is recorded for the ledger authority to post (money
        /// never moves here). Null on refusal — shortfalls dispense nothing.
        /// </summary>
        public GristMillMerchantSaleResult SellProductLot(string productKind, int units, string buyerId, string buyerName, int dayIndex, List<string> diag, int? pricePerUnitCents = null)
        {
            return GristMillMerchantTrade.SellProductLot(this, productKind, units, buyerId, buyerName, dayIndex, diag ?? diagnostics, pricePerUnitCents);
        }

        /// <summary>D2E: appends a booked merchant purchase to the mill's books.</summary>
        public void RecordMerchantPurchase(GristMillPurchaseRecord record)
        {
            if (record != null) purchaseHistory.Add(record);
        }

        /// <summary>D2E: appends a booked merchant sale to the mill's books.</summary>
        public void RecordMerchantSale(GristMillSaleRecord record)
        {
            if (record != null) saleHistory.Add(record);
        }

        /// <summary>W5A: merchant grain intake — the mill bought this grain.</summary>
        public string ReceiveMerchantLot(CropLot lot, List<string> diag)
        {
            return grainStock.ReceiveMerchantLot(lot, diag ?? diagnostics);
        }

        /// <summary>W5A: toll grain intake — the customer's grain, in custody.</summary>
        public string ReceiveTollLot(CropLot lot, string customerId, string customerName, int dayIndex, List<string> diag)
        {
            return grainStock.ReceiveTollLot(lot, customerId, customerName, dayIndex, diag ?? diagnostics);
        }

        /// <summary>
        /// W5A: registers ONLY the dress-millstones maintenance task. The
        /// "mill-grain" task registration stays owned by CRP-3
        /// (Miller.RegisterTaskDefinitions); duplicates are rejected
        /// deterministically, so this package never double-registers.
        /// </summary>
        public void RegisterTaskDefinitions(TaskAuthority authority, List<string> diag)
        {
            GrainMillMaintenanceTasks.RegisterTaskDefinitions(authority, diag ?? diagnostics);
        }

        /// <summary>
        /// Runs one grist batch: toll or merchant (policy data), wheat ->
        /// flour, corn -> meal, oats -> feed. Gate order: NX-1 workstation
        /// gate (Canon 4.1), then the §3.9 stone gate — both refuse loudly.
        /// A batch consumes from exactly ONE grain lot, so every product lot
        /// traces to exactly one farm's grain. Returns null on refusal;
        /// refused batches consume and wear nothing.
        ///
        /// Toll mode requires `tollLotId` (the customer's custody lot); the
        /// customer keeps the flour/meal (minus the in-kind toll) and the
        /// byproducts — the toll owed is recorded on the batch for the
        /// ledger authority to post. Merchant mode grinds the mill's own
        /// grain; the mill owns all products.
        /// </summary>
        public GristBatchResult RunGristBatch(
            GristBatchMode mode,
            CropKind crop,
            int units,
            EntityId workerId,
            int dayIndex,
            List<string> diag,
            string tollLotId = null,
            EquipmentTaskGate gate = null)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("GrainMillShopRuntime: no EntityIdRegistry — grinding refused.");
                return null;
            }
            if (units <= 0)
            {
                diag.Add("GrainMillShopRuntime: no grain requested.");
                return null;
            }
            var profile = GristMillConversionData.ProfileFor(crop);
            if (profile == null)
            {
                diag.Add($"GrainMillShopRuntime: {crop} has no grist profile — grinding refused (hay is not ground).");
                return null;
            }

            // NX-1A: Canon 4.1 — the millstones-and-power workstation, with
            // its support requirements (Canon 5.2: mill lubrication,
            // milling skill). Mirrors CRP-3 Miller.MillGrain gating.
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Workstation("grain-mill-station") },
                    "business", businessInstanceId, dayIndex, diag);
                if (blocked != null) return null;
            }

            // Tech X §3.9: dull/broken stones gate ALL output until dressed.
            if (stones.CheckStoneGate(dayIndex, diag) != null) return null;

            CropLot sourceLot;
            TollGrainCustodyLot tollCustody = null;
            if (mode == GristBatchMode.Toll)
            {
                if (string.IsNullOrWhiteSpace(tollLotId))
                {
                    diag.Add("GrainMillShopRuntime: toll mode requires the customer's toll lot id — anonymous toll grinding refused.");
                    return null;
                }
                var take = grainStock.TryTakeTollGrain(tollLotId, units, diag);
                if (take == null) return null;
                tollCustody = take.Value.Key;
                sourceLot = tollCustody.Lot;
                units = take.Value.Value;
            }
            else
            {
                var take = grainStock.TryTakeMerchantGrain(crop, units, diag);
                if (take == null) return null;
                sourceLot = take.Value.Key;
            }

            string sourceGrainLotId = sourceLot.LotId.ToString();
            int mainUnits = (units * profile.MainUnitsPer10) / 10;
            int feedUnits = (units * profile.FeedUnitsPer10) / 10;
            int branUnits = (units * profile.BranUnitsPer10) / 10;

            stones.ApplyWear(profile.StoneWearPer100Units01 * units / 100f);

            var record = new GristBatchRecord
            {
                BatchId = idRegistry.Allocate(EntityKind.Lot),
                Mode = mode,
                Crop = crop,
                GrainLotId = sourceGrainLotId,
                GrainUnitsIn = units,
                MainProductKind = profile.MainProductKind,
                MainUnits = mainUnits,
                FeedUnits = feedUnits,
                BranUnits = branUnits,
                WorkerId = workerId,
                DayIndex = dayIndex,
                MillerBusinessId = businessInstanceId,
            };

            if (mode == GristBatchMode.Toll)
            {
                // Toll form: the customer keeps the flour/meal; the mill
                // takes its toll (cash + in-kind share of the flour/meal).
                record.CustomerId = tollCustody.CustomerId;
                record.CustomerName = tollCustody.CustomerName;
                record.TollCashCents = units * Math.Max(0, policy.TollCashCentsPerUnit);
                record.TollInKindUnits = policy.TollInKindUnits(mainUnits);
                int customerMain = mainUnits - record.TollInKindUnits;

                if (record.TollInKindUnits > 0)
                {
                    ReceiveOwnedLot(MakeProductLot(
                        profile.MainProductKind, record.TollInKindUnits,
                        sourceGrainLotId, workerId, dayIndex,
                        $"in-kind toll from {tollCustody.CustomerName}"), diag);
                }
                if (customerMain > 0)
                {
                    tollOutbox.Add(new TollProductCustody
                    {
                        Lot = MakeProductLot(profile.MainProductKind, customerMain,
                            sourceGrainLotId, workerId, dayIndex, "toll grind"),
                        CustomerId = tollCustody.CustomerId,
                        CustomerName = tollCustody.CustomerName,
                    });
                }
                // Toll byproducts belong to the customer — held in custody,
                // not mill inventory.
                if (feedUnits > 0)
                {
                    tollOutbox.Add(new TollProductCustody
                    {
                        Lot = MakeProductLot("feed", feedUnits, sourceGrainLotId, workerId, dayIndex, "toll grind byproduct"),
                        CustomerId = tollCustody.CustomerId,
                        CustomerName = tollCustody.CustomerName,
                    });
                }
                if (branUnits > 0)
                {
                    tollOutbox.Add(new TollProductCustody
                    {
                        Lot = MakeProductLot("bran", branUnits, sourceGrainLotId, workerId, dayIndex, "toll grind byproduct"),
                        CustomerId = tollCustody.CustomerId,
                        CustomerName = tollCustody.CustomerName,
                    });
                }

                diag.Add($"GrainMillShopRuntime {businessInstanceId}: TOLL grind for {tollCustody.CustomerName} — "
                    + $"{units}u {crop} (lot {sourceGrainLotId}) -> {customerMain}u {profile.MainProductKind} to customer, "
                    + $"{record.TollInKindUnits}u in-kind toll + {record.TollCashCents}c cash toll to the mill, "
                    + $"{feedUnits}u feed + {branUnits}u bran to customer custody. Stones now {stones.Condition01:0.00}.");
            }
            else
            {
                // Merchant form: the mill owns everything it grinds.
                if (mainUnits > 0)
                {
                    ReceiveOwnedLot(MakeProductLot(
                        profile.MainProductKind, mainUnits, sourceGrainLotId, workerId, dayIndex, "merchant grind"), diag);
                }
                if (feedUnits > 0)
                {
                    ReceiveOwnedLot(MakeProductLot(
                        "feed", feedUnits, sourceGrainLotId, workerId, dayIndex, "merchant grind byproduct"), diag);
                }
                if (branUnits > 0)
                {
                    ReceiveOwnedLot(MakeProductLot(
                        "bran", branUnits, sourceGrainLotId, workerId, dayIndex, "merchant grind byproduct"), diag);
                }

                diag.Add($"GrainMillShopRuntime {businessInstanceId}: MERCHANT grind — "
                    + $"{units}u {crop} (lot {sourceGrainLotId}) -> {mainUnits}u {profile.MainProductKind} + "
                    + $"{feedUnits}u feed + {branUnits}u bran (mill-owned). Stones now {stones.Condition01:0.00}.");
            }

            if (stones.IsDull)
            {
                diag.Add($"GrainMillShopRuntime: stones '{stones.StoneId}' are now dull — the dresser must work before the next batch.");
            }

            batchHistory.Add(record);
            return new GristBatchResult { Record = record };
        }

        /// <summary>
        /// Dispenses mill-owned product units FIFO for a named buyer (the
        /// bakery takes flour via its own intake, preserving provenance).
        /// Returns the aggregated MillLot for the buyer, or null on
        /// shortfall — no units are conjured.
        /// </summary>
        public MillLot OfferProductLot(string productKind, int units, List<string> diag)
        {
            return productStock.TryDispenseProduct(idRegistry, productKind, units, businessInstanceId, diag ?? diagnostics);
        }

        /// <summary>
        /// Routes ALL mill-owned feed and bran lots to a named feed merchant
        /// (MillFeedSupplier) — real units move with named upstream
        /// (mill <- grain lot <- farm), never synthesized into the
        /// merchant's stock. Returns units routed.
        /// </summary>
        public int RouteFeedToSupplier(MillFeedSupplier supplier, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (supplier == null)
            {
                diag.Add("GrainMillShopRuntime: no feed supplier — byproduct lots stay in the mill yard.");
                return 0;
            }

            int feedUnits = productStock.UnitsOnHand("feed");
            int branUnits = productStock.UnitsOnHand("bran");
            int total = feedUnits + branUnits;
            if (total <= 0)
            {
                diag.Add("GrainMillShopRuntime: no feed/bran lots to route.");
                return 0;
            }

            var sources = new List<string>();
            foreach (var lot in productStock.Lots)
            {
                if ((string.Equals(lot.ProductKind, "feed", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(lot.ProductKind, "bran", StringComparison.OrdinalIgnoreCase))
                    && !sources.Contains(lot.SourceGrainLotId))
                {
                    sources.Add(lot.SourceGrainLotId);
                }
            }
            string upstream = $"mill {businessInstanceId} <- grain lot(s) {string.Join(";", sources.ToArray())} (CRP-3 grain chain)";
            string refusal = supplier.RestockFeed(total, upstream, dayIndex);
            if (refusal != null)
            {
                diag.Add($"GrainMillShopRuntime: feed supplier refused the restock — {refusal}. Lots stay in the mill yard.");
                return 0;
            }

            // The supplier accepted named units; now release them from stock.
            productStock.TryDispenseProduct(idRegistry, "feed", feedUnits, businessInstanceId, diag);
            productStock.TryDispenseProduct(idRegistry, "bran", branUnits, businessInstanceId, diag);
            diag.Add($"GrainMillShopRuntime: routed {feedUnits}u feed + {branUnits}u bran to feed merchant '{supplier.SupplierName}' — {upstream}.");
            return total;
        }

        /// <summary>
        /// The mill buys a toll customer's held feed/bran byproduct lots at
        /// the named price per unit. The lots become mill-owned product
        /// stock with provenance intact. Returns the purchase cost in cents
        /// for the ledger authority to post (money never moves here).
        /// </summary>
        public int BuyTollByproducts(string customerId, int pricePerUnitCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(customerId))
            {
                diag.Add("GrainMillShopRuntime: no customer named — toll byproduct purchase refused.");
                return 0;
            }

            var bought = new List<TollProductCustody>();
            int units = 0;
            foreach (var custody in tollOutbox)
            {
                if (string.Equals(custody.CustomerId, customerId, StringComparison.Ordinal)
                    && custody.Lot != null
                    && (string.Equals(custody.Lot.ProductKind, "feed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(custody.Lot.ProductKind, "bran", StringComparison.OrdinalIgnoreCase)))
                {
                    bought.Add(custody);
                    units += custody.Lot.QuantityUnits;
                }
            }
            if (bought.Count == 0)
            {
                diag.Add($"GrainMillShopRuntime: customer '{customerId}' holds no feed/bran byproduct lots — nothing to buy.");
                return 0;
            }

            foreach (var custody in bought)
            {
                string refusal = productStock.ReceiveLot(custody.Lot, diag);
                if (refusal == null) tollOutbox.Remove(custody);
                else diag.Add($"GrainMillShopRuntime: byproduct lot {custody.Lot.LotId} held back — {refusal}");
            }

            int costCents = units * Math.Max(0, pricePerUnitCents);
            diag.Add($"GrainMillShopRuntime: bought {units}u toll byproducts from {bought[0].CustomerName} for {costCents}c — posted by the ledger authority, not here.");
            return costCents;
        }

        /// <summary>
        /// The customer's pickup: releases all toll custody lots for the
        /// named customer. Returns the lots released.
        /// </summary>
        public List<MillLot> ReleaseTollProducts(string customerId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var released = new List<MillLot>();
            foreach (var custody in new List<TollProductCustody>(tollOutbox))
            {
                if (string.Equals(custody.CustomerId, customerId, StringComparison.Ordinal) && custody.Lot != null)
                {
                    released.Add(custody.Lot);
                    tollOutbox.Remove(custody);
                }
            }
            diag.Add($"GrainMillShopRuntime: released {released.Count} toll lot(s) to customer '{customerId}'.");
            return released;
        }

        /// <summary>W5A: the stone dresser's work — restores the stones, records the event.</summary>
        public string RecordStoneDressing(EntityId dresser, int dayIndex, List<string> diag)
        {
            return stones.RecordDressing(dresser, dayIndex, diag ?? diagnostics);
        }

        /// <summary>Takes a freshly-minted product lot into mill-owned stock, loudly.</summary>
        private void ReceiveOwnedLot(MillLot lot, List<string> diag)
        {
            string refusal = productStock.ReceiveLot(lot, diag);
            if (refusal != null)
            {
                diag.Add($"GrainMillShopRuntime: freshly-milled lot refused after grinding — {refusal}. "
                    + "The grain is consumed; this signals a lot-id collision, not a stock problem.");
            }
        }

        private MillLot MakeProductLot(string productKind, int units, string sourceGrainLotId,
            EntityId workerId, int dayIndex, string note)
        {
            var lot = new MillLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ProductKind = productKind,
                QuantityUnits = units,
                SourceGrainLotId = sourceGrainLotId ?? string.Empty,
                MillerBusinessId = businessInstanceId,
                MilledDayIndex = dayIndex,
                MilledBy = workerId,
            };
            return lot;
        }

        /// <summary>W5A save contract: lives inside the owning runtime class.</summary>
        [Serializable]
        public sealed class GrainMillShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public GrainMillGrainStock.GrainMillGrainStockSaveDto GrainStock = new GrainMillGrainStock.GrainMillGrainStockSaveDto();
            public GrainMillProductStock.GrainMillProductStockSaveDto ProductStock = new GrainMillProductStock.GrainMillProductStockSaveDto();
            public List<TollProductCustody> TollOutbox = new List<TollProductCustody>();
            public GrainMillStoneState.GrainMillStoneStateSaveDto Stones = new GrainMillStoneState.GrainMillStoneStateSaveDto();
            public GristMillPolicy Policy = new GristMillPolicy();
            public List<GristBatchRecord> BatchHistory = new List<GristBatchRecord>();
            public List<GristMillPurchaseRecord> PurchaseHistory = new List<GristMillPurchaseRecord>();
            public List<GristMillSaleRecord> SaleHistory = new List<GristMillSaleRecord>();
        }

        public GrainMillShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new GrainMillShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                GrainStock = grainStock.CaptureSaveDto(),
                ProductStock = productStock.CaptureSaveDto(),
                Stones = stones.CaptureSaveDto(),
                Policy = new GristMillPolicy
                {
                    TollCashCentsPerUnit = policy.TollCashCentsPerUnit,
                    TollInKindFlourShare01 = policy.TollInKindFlourShare01,
                    MerchantPrices = new List<GristMillPriceRow>(policy.MerchantPrices),
                },
                BatchHistory = new List<GristBatchRecord>(batchHistory),
                PurchaseHistory = new List<GristMillPurchaseRecord>(purchaseHistory),
                SaleHistory = new List<GristMillSaleRecord>(saleHistory),
            };
            foreach (var custody in tollOutbox)
            {
                if (custody == null || custody.Lot == null) continue;
                dto.TollOutbox.Add(new TollProductCustody
                {
                    Lot = new MillLot
                    {
                        LotId = custody.Lot.LotId,
                        ProductKind = custody.Lot.ProductKind,
                        QuantityUnits = custody.Lot.QuantityUnits,
                        SourceGrainLotId = custody.Lot.SourceGrainLotId,
                        MillerBusinessId = custody.Lot.MillerBusinessId,
                        MilledDayIndex = custody.Lot.MilledDayIndex,
                        MilledBy = custody.Lot.MilledBy,
                    },
                    CustomerId = custody.CustomerId,
                    CustomerName = custody.CustomerName,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(GrainMillShopRuntimeSaveDto dto)
        {
            grainStock.LoadFromSaveDto(dto != null ? dto.GrainStock : null);
            productStock.LoadFromSaveDto(dto != null ? dto.ProductStock : null);
            tollOutbox.Clear();
            if (dto != null && dto.TollOutbox != null)
            {
                foreach (var custody in dto.TollOutbox)
                {
                    if (custody == null || custody.Lot == null) continue;
                    tollOutbox.Add(custody);
                }
            }
            stones.LoadFromSaveDto(dto != null ? dto.Stones : null);
            if (dto != null && dto.Policy != null)
            {
                policy.TollCashCentsPerUnit = Math.Max(0, dto.Policy.TollCashCentsPerUnit);
                policy.TollInKindFlourShare01 = Mathf.Clamp01(dto.Policy.TollInKindFlourShare01);
                policy.MerchantPrices = new List<GristMillPriceRow>(dto.Policy.MerchantPrices ?? new List<GristMillPriceRow>());
            }
            batchHistory.Clear();
            if (dto != null && dto.BatchHistory != null)
            {
                batchHistory.AddRange(dto.BatchHistory);
            }
            purchaseHistory.Clear();
            if (dto != null && dto.PurchaseHistory != null)
            {
                foreach (var record in dto.PurchaseHistory)
                {
                    if (record != null) purchaseHistory.Add(record);
                }
            }
            saleHistory.Clear();
            if (dto != null && dto.SaleHistory != null)
            {
                foreach (var record in dto.SaleHistory)
                {
                    if (record != null) saleHistory.Add(record);
                }
            }
        }
    }
}
