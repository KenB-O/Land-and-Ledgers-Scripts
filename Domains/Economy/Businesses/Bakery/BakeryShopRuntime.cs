using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// W2A: the production stages of a dough batch. Batches flow
    /// Planned → Prepped → Baked. A batch stays parked at its stage on any
    /// shortfall — never worked on conjured stock, never silently dropped.
    /// </summary>
    public enum BakeryBatchStage
    {
        Planned = 0,
        Prepped = 1,
        Baked = 2,
        Scrapped = 3,
    }

    /// <summary>
    /// W2A: one dough batch. The batch holds its flour and notions in custody
    /// from the prep stage (dispensed from named lots with provenance lines),
    /// so baking never re-dispenses. The custody lines move into the baked
    /// bread lot's provenance at baking.
    /// </summary>
    [Serializable]
    public sealed class BakeryDoughBatch
    {
        public string BatchId = string.Empty;
        public string ProductId = string.Empty; // bake.bread-loaf / bake.rolls / bake.pie
        public int ScheduledDayIndex;
        public BakeryBatchStage Stage = BakeryBatchStage.Planned;

        /// <summary>Flour dispensed into this batch's custody at prep, with provenance.</summary>
        public List<BakeryFlourDispenseLine> FlourInCustody = new List<BakeryFlourDispenseLine>();

        /// <summary>Notions dispensed at prep, with provenance.</summary>
        public List<BakeryFlourDispenseLine> NotionsUsed = new List<BakeryFlourDispenseLine>();

        public List<string> StageLog = new List<string>();

        public BakeryDoughBatch() { }

        public bool IsActive => Stage == BakeryBatchStage.Planned || Stage == BakeryBatchStage.Prepped;
    }

    /// <summary>
    /// W2A: staling bands for finished baked goods. Persistent degradation, not
    /// an expiry timer (GHOST-CAN-010): day-old bread is still sold (at a
    /// discount — GHOST-CAN-011, deterioration hurts price before total loss),
    /// but stale bread is waste and can never be sold. Lots never mix, so old
    /// stock is never rejuvenated by new baking (GHOST-TECH-002).
    /// </summary>
    public enum BakeryGoodsCondition
    {
        Unspecified = 0,
        Fresh = 1,   // Baked today. Full price.
        DayOld = 2,  // Baked yesterday. Discounted; sell it today.
        Stale = 3,   // Waste. Never sold.
    }

    /// <summary>
    /// W2A: one finished-goods lot — a baked batch's output with its own baked
    /// day and condition. Staling is per-lot; new lots never refresh old ones.
    /// </summary>
    [Serializable]
    public sealed class BakeryBreadLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string ProductId = string.Empty;
        public int BakedDayIndex;
        public int UnitCount;
        public BakeryGoodsCondition Condition = BakeryGoodsCondition.Fresh;

        /// <summary>Upstream flour/notions chains baked into this lot (full provenance).</summary>
        public List<string> InputProvenance = new List<string>();

        public BakeryBreadLot() { }

        /// <summary>
        /// Ages the lot to the given day. Bread stales fast: the day it is
        /// baked it is fresh, the next day it is day-old (discounted), after
        /// that it is stale waste. Stale lots stay on the books with provenance
        /// retained for audit — they are never sold and never silently vanish.
        /// </summary>
        public void AgeToDay(int dayIndex)
        {
            int age = dayIndex - BakedDayIndex;
            Condition = age <= 0 ? BakeryGoodsCondition.Fresh
                : age == 1 ? BakeryGoodsCondition.DayOld
                : BakeryGoodsCondition.Stale;
        }

        public bool CanSell => Condition != BakeryGoodsCondition.Stale && UnitCount > 0;

        public int TakeUnits(int requested)
        {
            int taken = Math.Min(Math.Max(0, requested), UnitCount);
            UnitCount -= taken;
            return taken;
        }
    }

    /// <summary>
    /// W2A: one bread sale — the retail record. The caller settles these as
    /// ordinary ledger outflows; money moves only through ledger authorities.
    /// </summary>
    [Serializable]
    public sealed class BakerySaleRecord
    {
        public string SaleId = string.Empty;
        public EntityId LotId = EntityId.Invalid;
        public string ProductId = string.Empty;
        public int DayIndex;
        public int Units;
        public int PricePerUnitCents;
        public BakeryGoodsCondition ConditionSoldAt = BakeryGoodsCondition.Fresh;
        public string ProvenanceChain = string.Empty;

        public BakerySaleRecord() { }

        public int TotalCents => Math.Max(0, Units) * Math.Max(0, PricePerUnitCents);
    }

    /// <summary>
    /// W2A: one stale-bread write-off. Stale lots stay on the books as waste
    /// (never sold), and the write-off appears plainly for the ledger.
    /// </summary>
    [Serializable]
    public sealed class BakeryWasteRecord
    {
        public EntityId LotId = EntityId.Invalid;
        public string ProductId = string.Empty;
        public int Units;
        public int BakedDayIndex;
        public int WastedDayIndex;
        public string ProvenanceChain = string.Empty;

        public BakeryWasteRecord() { }
    }

    /// <summary>
    /// W2A: one bake oven. An oven IS a workstation instance (WorkstationId
    /// "bake-oven", Tech X §3.5: BakeOven): readiness derives from its
    /// component assets (oven chamber, kneading table, proofing rack,
    /// bake peels), never from a flag. Each ready oven offers
    /// <see cref="BakeryBreadCatalog.FiringsPerOvenPerDay"/> firings per day —
    /// the daily bake is capacity-gated by ready ovens.
    /// </summary>
    [Serializable]
    public sealed class BakeryOven
    {
        public int OvenIndex;
        public WorkstationInstance Station = new WorkstationInstance();

        public BakeryOven() { }
    }

    /// <summary>
    /// W2A: the per-instance bakery runtime. Holds the oven pool (ovens as
    /// workstations gating daily bake capacity), the flour bin (lots with
    /// provenance), the retail price schedule, the dough-batch queue, the
    /// finished-goods shelf (staling lots), and the day-resolution scheduler
    /// that makes the daily-bake problem visible: batches flow plan → prep
    /// (flour dispensed into custody) → bake (one firing per batch on a ready
    /// oven) while labor minutes last, and baked goods stale across days.
    ///
    /// Boundary: this runtime owns the oven/flour/batch/shelf layer only. Any
    /// generic resolution for BusinessType.Bakery in
    /// SharedBusinessRuntimeManager is untouched — W2A routes around it the
    /// way W1C routes around the tailor's shared resolution.
    /// </summary>
    public sealed class BakeryShopRuntime
    {
        /// <summary>TUNING: the baker's hands-on minutes per day (calibration, Canon Part XV; mirrors the tailor's professional day).</summary>
        public const int BakerMinutesPerDay = 600;

        private readonly string businessInstanceId;
        private readonly BakeryFlourStock flourStock = new BakeryFlourStock();
        private readonly List<BakeryOven> ovens = new List<BakeryOven>();
        private readonly List<BakeryDoughBatch> batches = new List<BakeryDoughBatch>();
        private readonly List<BakeryBreadLot> breadShelf = new List<BakeryBreadLot>();
        private readonly List<BakerySaleRecord> sales = new List<BakerySaleRecord>();
        private readonly List<BakeryWasteRecord> waste = new List<BakeryWasteRecord>();
        private readonly List<string> wasteNotedLotIds = new List<string>();
        private BakeryPriceSchedule prices = new BakeryPriceSchedule();
        private int batchSeq;
        private int saleSeq;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public BakeryFlourStock FlourStock => flourStock;
        public IReadOnlyList<BakeryOven> Ovens => ovens;
        public IReadOnlyList<BakeryDoughBatch> Batches => batches;
        public IReadOnlyList<BakeryBreadLot> BreadShelf => breadShelf;
        public IReadOnlyList<BakerySaleRecord> Sales => sales;
        public IReadOnlyList<BakeryWasteRecord> Waste => waste;
        public BakeryPriceSchedule Prices => prices;

        public BakeryShopRuntime(string businessInstanceId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
        }

        public void SetPriceSchedule(BakeryPriceSchedule schedule)
        {
            prices = schedule ?? new BakeryPriceSchedule();
        }

        /// <summary>
        /// Installs a bake oven: a workstation instance of "bake-oven" with its
        /// component assets named. Returns a rejection string, or null on
        /// success (the oven index is ovens.Count - 1 afterwards).
        /// </summary>
        public string AddOven(string spaceId, List<string> componentAssetIds, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(spaceId))
                return "BakeryShopRuntime: a bake oven needs a functional space — a room alone never grants the workstation.";

            var oven = new BakeryOven
            {
                OvenIndex = ovens.Count,
                Station = new WorkstationInstance
                {
                    InstanceId = $"{businessInstanceId}-bake-oven-{ovens.Count}",
                    WorkstationId = BakeryBreadCatalog.BakeOvenStationId,
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = spaceId,
                },
            };
            if (componentAssetIds != null)
            {
                foreach (string assetId in componentAssetIds)
                {
                    oven.Station.InstallComponent(assetId);
                }
            }

            ovens.Add(oven);
            diag.Add($"BakeryShopRuntime: bake oven {oven.OvenIndex} installed in '{spaceId}' (workstation {oven.Station.InstanceId}).");
            return null;
        }

        /// <summary>
        /// Plans dough batches of a product. Returns the batch ids, or null with
        /// a LOUD diagnostic — callers tell them apart (ids never contain the
        /// "BakeryShopRuntime:" prefix). One batch = one firing's worth of
        /// product; no flour is dispensed yet — that happens at prep.
        /// </summary>
        public List<string> PlanBatch(string productId, int batchCount, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!BakeryBreadCatalog.IsKnownProduct(productId))
                return PlanRefused(diag, $"unknown product '{productId}' — only bread loaves, rolls, and pies are baked.");
            if (batchCount <= 0)
                return PlanRefused(diag, "batch count must be positive — nothing planned.");

            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(productId);
            var ids = new List<string>();
            for (int i = 0; i < batchCount; i++)
            {
                var batch = new BakeryDoughBatch
                {
                    BatchId = $"{businessInstanceId}-batch-{batchSeq++}",
                    ProductId = productId,
                    ScheduledDayIndex = dayIndex,
                    Stage = BakeryBatchStage.Planned,
                };
                batch.StageLog.Add($"day {dayIndex}: batch planned ({spec.DisplayName}, {spec.YieldUnitsPerBatch} {spec.UnitName}s)");
                batches.Add(batch);
                ids.Add(batch.BatchId);
            }

            diag.Add($"BakeryShopRuntime: planned {batchCount} dough batch(es) of {spec.DisplayName} (day {dayIndex}).");
            return ids;
        }

        private List<string> PlanRefused(List<string> diag, string reason)
        {
            string message = $"BakeryShopRuntime: batch planning refused — {reason}";
            diag.Add(message);
            return null;
        }

        /// <summary>
        /// Counts ovens whose workstation instance evaluates ready against the
        /// catalog definition. Loud per-oven diagnostics — an unready oven is
        /// named, never silently skipped.
        /// </summary>
        public int CountReadyOvens(
            WorkstationDefinition ovenDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            int ready = 0;
            foreach (var oven in ovens)
            {
                var reasons = new List<string>();
                string notReady = oven.Station.EvaluateReady(ovenDefinition, findComponent, reasons);
                if (notReady == null)
                {
                    ready++;
                }
                else
                {
                    diag.Add($"BakeryShopRuntime: bake oven {oven.OvenIndex} not ready — {notReady}");
                }
            }

            return ready;
        }

        /// <summary>
        /// Runs one bakery day:
        /// 1. Ages the finished-goods shelf (staling) — newly stale lots are
        ///    written off as waste, loudly, with provenance retained.
        /// 2. Advances planned batches FIFO: prep dispenses flour and notions
        ///    into the batch's custody (loud refusal on shortfall — the batch
        ///    stays planned), then baking claims one firing on a ready oven.
        ///    Stages cascade while the baker's labor minutes last.
        /// Each WorkDay call is one day: firing budgets reset at its start.
        /// Returns the number of batches baked. The id registry is optional:
        /// when provided, baked bread lots take real HF-1 lot ids; without it
        /// the lot is still a real commercial lot but its id is honestly
        /// reported as unregistered (GrainDealer.SellGrain T1A precedent).</summary>
        public int WorkDay(
            int dayIndex,
            bool bakerKitUsable,
            WorkstationDefinition ovenDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag,
            EntityIdRegistry idRegistry = null)
        {
            diag = diag ?? diagnostics;

            AgeShelf(dayIndex, diag);

            if (!bakerKitUsable)
            {
                diag.Add($"BakeryShopRuntime: no usable baker's hand kit — the shop cannot work today (NX-1 teeth gate). {CountActiveBatches()} batch(es) still open.");
                return 0;
            }

            // Ready ovens and their firing budgets for today.
            var ovenFiringsLeft = new Dictionary<int, int>();
            foreach (var oven in ovens)
            {
                var reasons = new List<string>();
                if (oven.Station.EvaluateReady(ovenDefinition, findComponent, reasons) == null)
                {
                    ovenFiringsLeft[oven.OvenIndex] = BakeryBreadCatalog.FiringsPerOvenPerDay;
                }
            }

            if (ovenFiringsLeft.Count == 0)
            {
                diag.Add("BakeryShopRuntime: no ready bake oven — Tech X §3.5: baking requires the oven; nothing bakes today.");
            }

            int laborRemaining = BakerMinutesPerDay;
            int baked = 0;
            var snapshot = new List<BakeryDoughBatch>(batches);

            foreach (var batch in snapshot)
            {
                if (!batch.IsActive) continue;

                while (TryAdvanceOneBatch(batch, dayIndex, ref laborRemaining, ovenFiringsLeft, diag, idRegistry))
                {
                    baked++;
                }
            }

            return baked;
        }

        /// <summary>
        /// Ages every finished-goods lot to the given day. Newly stale lots get
        /// one loud write-off each (deduped across days and save/load cycles);
        /// stale lots stay on the books as waste — never sold, never silently
        /// removed, provenance retained (GHOST-TECH-002).
        /// </summary>
        public void AgeShelf(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (var lot in breadShelf)
            {
                if (lot == null) continue;
                lot.AgeToDay(dayIndex);
                if (lot.Condition == BakeryGoodsCondition.Stale
                    && lot.UnitCount > 0
                    && !wasteNotedLotIds.Contains(lot.LotId.ToString()))
                {
                    var chains = new List<string>();
                    if (lot.InputProvenance != null) chains.AddRange(lot.InputProvenance);
                    waste.Add(new BakeryWasteRecord
                    {
                        LotId = lot.LotId,
                        ProductId = lot.ProductId,
                        Units = lot.UnitCount,
                        BakedDayIndex = lot.BakedDayIndex,
                        WastedDayIndex = dayIndex,
                        ProvenanceChain = string.Join(" | ", chains),
                    });
                    wasteNotedLotIds.Add(lot.LotId.ToString());
                    diag.Add($"BakeryShopRuntime: lot {lot.LotId} ({lot.ProductId}, {lot.UnitCount} units, baked day {lot.BakedDayIndex}) went stale — written off as waste (day {dayIndex}). Never sold.");
                }
            }
        }

        /// <summary>
        /// Advances one batch one stage if its preconditions hold. Returns true
        /// when the batch finished BAKING (the caller counts it); prep-only
        /// advances return false so a prep-followed-by-bake in one day counts
        /// exactly one bake.
        /// </summary>
        private bool TryAdvanceOneBatch(
            BakeryDoughBatch batch, int dayIndex, ref int laborRemaining,
            Dictionary<int, int> ovenFiringsLeft, List<string> diag,
            EntityIdRegistry idRegistry)
        {
            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(batch.ProductId);
            if (string.IsNullOrEmpty(spec.ProductId))
            {
                diag.Add($"BakeryShopRuntime: batch {batch.BatchId} names unknown product '{batch.ProductId}' — stays parked, never guessed.");
                return false;
            }

            switch (batch.Stage)
            {
                case BakeryBatchStage.Planned:
                    // Prep: flour and notions into the batch's custody, FIFO lots.
                    var flourLines = flourStock.TryDispenseUnits(
                        BakeryBreadCatalog.FlourItemId, spec.FlourUnitsPerBatch, dayIndex, diag);
                    if (flourLines == null)
                    {
                        diag.Add($"BakeryShopRuntime: batch {batch.BatchId} ({spec.DisplayName}) refused — no flour on hand. Stays planned.");
                        return false;
                    }

                    var notionLines = flourStock.TryDispenseUnits(
                        BakeryBreadCatalog.NotionsItemId, spec.NotionUnitsPerBatch, dayIndex, diag);
                    if (notionLines == null)
                    {
                        // Flour was already dispensed — honesty requires it back.
                        // Returned flour goes on as a fresh lot naming the
                        // reversal; never silently absorbed.
                        ReturnCustodyToBin(batch, flourLines, dayIndex, diag, idRegistry);
                        diag.Add($"BakeryShopRuntime: batch {batch.BatchId} ({spec.DisplayName}) refused — no notions on hand. Flour returned to the bin. Stays planned.");
                        return false;
                    }

                    if (!SpendLabor(ref laborRemaining, spec.PrepMinutes, diag, batch, "prepping"))
                    {
                        ReturnCustodyToBin(batch, flourLines, dayIndex, diag, idRegistry);
                        ReturnCustodyToBin(batch, notionLines, dayIndex, diag, idRegistry);
                        diag.Add($"BakeryShopRuntime: batch {batch.BatchId} prep needs {spec.PrepMinutes}m — stays planned for tomorrow.");
                        return false;
                    }

                    batch.FlourInCustody.AddRange(flourLines);
                    batch.NotionsUsed.AddRange(notionLines);
                    SetStage(batch, BakeryBatchStage.Prepped, dayIndex,
                        $"prepped ({spec.PrepMinutes}m, {spec.FlourUnitsPerBatch} lb flour from {flourLines.Count} lot(s))");
                    // Prepped — cascade into baking below (falls through by
                    // recursion, not fallthrough; stage is now Prepped).
                    break;

                case BakeryBatchStage.Prepped:
                    int ovenIndex = ClaimFiring(ovenFiringsLeft);
                    if (ovenIndex < 0)
                    {
                        diag.Add($"BakeryShopRuntime: batch {batch.BatchId} ({spec.DisplayName}) waits on an oven firing — all ready ovens are fully booked today. Stays prepped.");
                        return false;
                    }

                    if (!SpendLabor(ref laborRemaining, spec.BakeMinutes, diag, batch, "baking"))
                    {
                        ovenFiringsLeft[ovenIndex]++;
                        diag.Add($"BakeryShopRuntime: batch {batch.BatchId} bake needs {spec.BakeMinutes}m — stays prepped for tomorrow.");
                        return false;
                    }

                    BakeBatch(batch, spec, ovenIndex, dayIndex, diag, idRegistry);
                    return true;

                default:
                    return false;
            }

            // A batch that just prepped cascades into the bake stage immediately.
            return TryAdvanceOneBatch(batch, dayIndex, ref laborRemaining, ovenFiringsLeft, diag, idRegistry);
        }

        /// <summary>
        /// Returns just-dispensed custody lines to the bin as a fresh lot
        /// naming the reversal — provenance is preserved, never silently
        /// absorbed. The returned lot takes a real lot id when the caller
        /// provides the registry; without one its id is honestly reported as
        /// unregistered (GrainDealer.SellGrain T1A precedent).
        /// </summary>
        private void ReturnCustodyToBin(
            BakeryDoughBatch batch, List<BakeryFlourDispenseLine> lines, int dayIndex,
            List<string> diag, EntityIdRegistry idRegistry)
        {
            if (lines == null) return;
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var chains = new List<string>();
            foreach (var line in lines)
            {
                if (line == null) continue;
                if (!byName.TryGetValue(line.FlourName, out int soFar)) soFar = 0;
                byName[line.FlourName] = soFar + Math.Max(0, line.UnitsTaken);
                if (!string.IsNullOrWhiteSpace(line.ProvenanceChain)) chains.Add(line.ProvenanceChain);
            }

            foreach (var kvp in byName)
            {
                string rejection = flourStock.ReceiveLot(new BakeryFlourLot
                {
                    LotId = idRegistry != null ? idRegistry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                    FlourName = kvp.Key,
                    Units = kvp.Value,
                    AcquiredDayIndex = dayIndex,
                    ImportOrderId = $"return-{batch.BatchId}",
                    OriginName = "Returned to bin from failed batch prep",
                    SupplierNote = "Originally: " + string.Join(" | ", chains),
                    IsBootstrapEndowment = false,
                }, diag);
                if (rejection != null)
                {
                    diag.Add($"BakeryShopRuntime: custody return for batch {batch.BatchId} refused — {rejection}");
                }
            }
        }

        /// <summary>Bakes a prepped batch: one firing → one finished-goods lot with full input provenance.</summary>
        private void BakeBatch(
            BakeryDoughBatch batch, BakeryProductSpec spec, int ovenIndex, int dayIndex,
            List<string> diag, EntityIdRegistry idRegistry)
        {
            var lot = new BakeryBreadLot
            {
                // A real HF-1 lot id when the caller provides the registry;
                // without one the lot is still a real commercial lot but its id
                // is honestly reported as unregistered (T1A precedent).
                LotId = idRegistry != null ? idRegistry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                ProductId = batch.ProductId,
                BakedDayIndex = dayIndex,
                UnitCount = spec.YieldUnitsPerBatch,
                Condition = BakeryGoodsCondition.Fresh,
            };
            foreach (var line in batch.FlourInCustody)
            {
                if (line != null) lot.InputProvenance.Add($"{line.UnitsTaken}× {line.FlourName} from {line.ProvenanceChain}");
            }

            foreach (var line in batch.NotionsUsed)
            {
                if (line != null) lot.InputProvenance.Add($"{line.UnitsTaken}× {line.FlourName} from {line.ProvenanceChain}");
            }

            breadShelf.Add(lot);
            // The inputs are now IN the bread: custody lines move to the lot, so
            // the batch never double-counts and the bin never sees them again.
            batch.FlourInCustody.Clear();
            batch.NotionsUsed.Clear();

            SetStage(batch, BakeryBatchStage.Baked, dayIndex,
                $"baked on oven {ovenIndex} ({spec.BakeMinutes}m) → {spec.YieldUnitsPerBatch} {spec.UnitName}s");
            diag.Add($"BakeryShopRuntime: batch {batch.BatchId} baked — {spec.YieldUnitsPerBatch} {spec.UnitName}s of {spec.DisplayName} (day {dayIndex}, oven {ovenIndex}).");
        }

        /// <summary>
        /// Sells bread FIFO by baked day (oldest first — day-old sells before
        /// fresh, so nothing good is wasted while day-old sits). Day-old lots
        /// price at the schedule's day-old discount automatically; stale lots
        /// are never sold. Returns the sale records; the caller settles them
        /// as ordinary ledger outflows.
        /// </summary>
        public List<BakerySaleRecord> SellBread(string productId, int unitsRequested, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var records = new List<BakerySaleRecord>();
            if (!BakeryBreadCatalog.IsKnownProduct(productId))
            {
                diag.Add($"BakeryShopRuntime: sale refused — unknown product '{productId}'.");
                return records;
            }

            if (unitsRequested <= 0)
            {
                diag.Add("BakeryShopRuntime: sale refused — positive unit count required.");
                return records;
            }

            AgeShelf(dayIndex, diag);

            var ordered = new List<BakeryBreadLot>();
            foreach (var lot in breadShelf)
            {
                if (lot != null
                    && lot.CanSell
                    && string.Equals(lot.ProductId, productId, StringComparison.Ordinal))
                {
                    ordered.Add(lot);
                }
            }

            ordered.Sort((a, b) => a.BakedDayIndex.CompareTo(b.BakedDayIndex));

            int remaining = unitsRequested;
            foreach (var lot in ordered)
            {
                if (remaining <= 0) break;
                int taken = lot.TakeUnits(remaining);
                if (taken <= 0) continue;
                remaining -= taken;

                int pricePerUnit = lot.ConditionSoldAtPrice(prices);
                records.Add(new BakerySaleRecord
                {
                    SaleId = $"{businessInstanceId}-sale-{saleSeq++}",
                    LotId = lot.LotId,
                    ProductId = lot.ProductId,
                    DayIndex = dayIndex,
                    Units = taken,
                    PricePerUnitCents = pricePerUnit,
                    ConditionSoldAt = lot.Condition,
                    ProvenanceChain = lot.InputProvenance != null
                        ? string.Join(" | ", lot.InputProvenance)
                        : string.Empty,
                });
            }

            int sold = unitsRequested - remaining;
            if (sold < unitsRequested)
            {
                diag.Add($"BakeryShopRuntime: only {sold}/{unitsRequested} {productId} units sold — finite supply (Canon §9.2).");
            }
            else
            {
                diag.Add($"BakeryShopRuntime: sold {sold} unit(s) of {productId} (day {dayIndex}).");
            }

            return records;
        }

        private bool SpendLabor(ref int laborRemaining, int minutes, List<string> diag, BakeryDoughBatch batch, string stageName)
        {
            if (minutes <= 0) return true;
            if (laborRemaining < minutes)
            {
                diag.Add($"BakeryShopRuntime: batch {batch.BatchId} {stageName} needs {minutes}m, only {laborRemaining}m left today.");
                return false;
            }

            laborRemaining -= minutes;
            return true;
        }

        /// <summary>
        /// Claims one firing on the lowest-index ready oven with a free firing.
        /// Deterministic; returns -1 (with a diagnostic left to the caller) when
        /// every ready oven is fully booked today.
        /// </summary>
        private static int ClaimFiring(Dictionary<int, int> ovenFiringsLeft)
        {
            int best = int.MaxValue;
            foreach (var kvp in ovenFiringsLeft)
            {
                if (kvp.Value > 0 && kvp.Key < best) best = kvp.Key;
            }

            if (best == int.MaxValue) return -1;
            ovenFiringsLeft[best]--;
            return best;
        }

        private void SetStage(BakeryDoughBatch batch, BakeryBatchStage stage, int dayIndex, string note)
        {
            batch.Stage = stage;
            batch.StageLog.Add($"day {dayIndex}: → {stage} ({note})");
        }

        private int CountActiveBatches()
        {
            int count = 0;
            foreach (var batch in batches)
            {
                if (batch.IsActive) count++;
            }

            return count;
        }

        /// <summary>
        /// Exposes the bakery's ovens to the NX-1 equipment gate: each oven is
        /// registered under "bake-oven-{n}", and the declared "bake-oven" id
        /// aliases the first READY oven at call time (point-in-time resolution —
        /// the runtime stays the authority for per-batch firing assignment).
        /// With no ready oven the alias is left unregistered so the gate
        /// refuses honestly.
        /// </summary>
        public void PopulateWorkstations(
            BusinessWorkstations registry,
            WorkstationDefinition ovenDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (registry == null)
            {
                diag.Add("BakeryShopRuntime: no workstation registry — bake ovens not exposed to the equipment gate.");
                return;
            }

            BakeryOven firstReady = null;
            foreach (var oven in ovens)
            {
                registry.RegisterInstance(new WorkstationInstance
                {
                    InstanceId = $"{businessInstanceId}-bake-oven-{oven.OvenIndex}",
                    WorkstationId = $"{BakeryBreadCatalog.BakeOvenStationId}-{oven.OvenIndex}",
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = oven.Station.SpaceId,
                    ComponentAssetIds = new List<string>(oven.Station.ComponentAssetIds),
                });

                if (firstReady == null)
                {
                    var reasons = new List<string>();
                    if (oven.Station.EvaluateReady(ovenDefinition, findComponent, reasons) == null)
                    {
                        firstReady = oven;
                    }
                }
            }

            if (firstReady != null)
            {
                registry.RegisterInstance(new WorkstationInstance
                {
                    InstanceId = firstReady.Station.InstanceId,
                    WorkstationId = BakeryBreadCatalog.BakeOvenStationId,
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = firstReady.Station.SpaceId,
                    ComponentAssetIds = new List<string>(firstReady.Station.ComponentAssetIds),
                });
                diag.Add($"BakeryShopRuntime: '{BakeryBreadCatalog.BakeOvenStationId}' resolves to oven {firstReady.OvenIndex} (first ready, point-in-time).");
            }
            else
            {
                diag.Add("BakeryShopRuntime: no ready oven — the workstation alias is left unregistered; the gate refuses honestly.");
            }
        }

        /// <summary>Throughput readout: the oven-and-labor problem, plainly stated.</summary>
        public string CoverageSummary()
        {
            return $"Bakery {businessInstanceId}: {ovens.Count} oven(s), " +
                $"{CountActiveBatches()} open batch(es), {breadShelf.Count} bread lot(s) on the shelf, " +
                $"{flourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId)} lb flour on hand.";
        }

        public void CloseDay(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            diag.Add($"Bakery {businessInstanceId}: day {dayIndex} — {CoverageSummary()}");
        }

        #region Save / Load
        [Serializable]
        public sealed class BakeryShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public BakeryPriceSchedule Prices = new BakeryPriceSchedule();
            public BakeryFlourStock.BakeryFlourStockSaveDto FlourStock = new BakeryFlourStock.BakeryFlourStockSaveDto();
            public List<BakeryOven> Ovens = new List<BakeryOven>();
            public List<BakeryDoughBatch> Batches = new List<BakeryDoughBatch>();
            public List<BakeryBreadLot> BreadShelf = new List<BakeryBreadLot>();
            public List<BakerySaleRecord> Sales = new List<BakerySaleRecord>();
            public List<BakeryWasteRecord> Waste = new List<BakeryWasteRecord>();
            public List<string> WasteNotedLotIds = new List<string>();
            public int BatchSeq;
            public int SaleSeq;
        }

        public BakeryShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new BakeryShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                Prices = prices,
                FlourStock = flourStock.CaptureSaveDto(),
                BatchSeq = batchSeq,
                SaleSeq = saleSeq,
            };
            dto.Ovens.AddRange(ovens);
            // Only live batches rehydrate; baked batches are history and their
            // bread lots (below) are the persisted audit trail.
            foreach (var batch in batches)
            {
                if (batch != null && batch.IsActive) dto.Batches.Add(batch);
            }

            dto.BreadShelf.AddRange(breadShelf);
            dto.Sales.AddRange(sales);
            dto.Waste.AddRange(waste);
            dto.WasteNotedLotIds.AddRange(wasteNotedLotIds);
            return dto;
        }

        public void LoadFromSaveDto(BakeryShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            flourStock.LoadFromSaveDto(dto.FlourStock);
            if (dto.Prices != null) prices = dto.Prices;
            batchSeq = Math.Max(0, dto.BatchSeq);
            saleSeq = Math.Max(0, dto.SaleSeq);
            ovens.Clear();
            if (dto.Ovens != null)
            {
                foreach (var oven in dto.Ovens)
                {
                    if (oven == null) continue;
                    if (oven.Station == null) oven.Station = new WorkstationInstance();
                    ovens.Add(oven);
                }
            }

            batches.Clear();
            if (dto.Batches != null)
            {
                foreach (var batch in dto.Batches)
                {
                    if (batch != null) batches.Add(batch);
                }
            }

            breadShelf.Clear();
            if (dto.BreadShelf != null)
            {
                foreach (var lot in dto.BreadShelf)
                {
                    if (lot != null) breadShelf.Add(lot);
                }
            }

            sales.Clear();
            if (dto.Sales != null)
            {
                foreach (var sale in dto.Sales)
                {
                    if (sale != null) sales.Add(sale);
                }
            }

            waste.Clear();
            if (dto.Waste != null)
            {
                foreach (var record in dto.Waste)
                {
                    if (record != null) waste.Add(record);
                }
            }

            wasteNotedLotIds.Clear();
            if (dto.WasteNotedLotIds != null)
            {
                wasteNotedLotIds.AddRange(dto.WasteNotedLotIds);
            }
        }
        #endregion
    }

    /// <summary>
    /// W2A: condition-aware pricing helper for finished-goods lots. Keeps the
    /// staling price logic next to the lot type.
    /// </summary>
    public static class BakeryGoodsPricing
    {
        /// <summary>
        /// The price a finished-goods lot sells for at its CURRENT condition:
        /// fresh → fresh price; day-old → schedule discount; stale → zero
        /// (stale lots never sell anyway — this is the guard, not the plan).
        /// </summary>
        public static int ConditionSoldAtPrice(this BakeryBreadLot lot, BakeryPriceSchedule prices)
        {
            if (lot == null) return 0;
            switch (lot.Condition)
            {
                case BakeryGoodsCondition.Fresh:
                    return BakeryBreadCatalog.GetFreshPriceCents(lot.ProductId, prices);
                case BakeryGoodsCondition.DayOld:
                    return BakeryBreadCatalog.GetDayOldPriceCents(lot.ProductId, prices);
                default:
                    return 0;
            }
        }
    }
}
