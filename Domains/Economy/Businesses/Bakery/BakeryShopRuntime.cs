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
    /// Planned → Prepped → Proofing → Baked. A batch stays parked at its stage
    /// on any shortfall — never worked on conjured stock, never silently
    /// dropped.
    ///
    /// D1D: Proofing sits between prep and bake (Canon Part III §3.1 names
    /// proof as a meaningful bakery task; the canon equipment profile lists
    /// the proofing rack). Proof consumes rack time, not baker labor — see
    /// <see cref="BakeryProofScheduler"/>. Proofing is appended (value 4) so
    /// existing saved values for Baked/Scrapped never renumber.
    /// </summary>
    public enum BakeryBatchStage
    {
        Planned = 0,
        Prepped = 1,
        Baked = 2,
        Scrapped = 3,
        Proofing = 4,
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

        /// <summary>
        /// D1D: proof minutes still required before this batch may bake. Set
        /// from the product spec when the batch enters Proofing; reduced by the
        /// proof scheduler's daily rack budget.
        /// </summary>
        public int ProofMinutesRemaining;

        /// <summary>
        /// D1D: proof-entry order — the proof scheduler allocates rack budget
        /// FIFO by this sequence.
        /// </summary>
        public int ProofOrder = -1;

        public List<string> StageLog = new List<string>();

        public BakeryDoughBatch() { }

        public bool IsActive => Stage == BakeryBatchStage.Planned
            || Stage == BakeryBatchStage.Prepped
            || Stage == BakeryBatchStage.Proofing;
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
        /// <summary>
        /// TUNING: the baker's hands-on minutes per day (calibration, Canon
        /// Part XV; mirrors the tailor's professional day).
        ///
        /// D1D WIDTH-FIX: raised 600 → 840. The W2A oven-capacity test
        /// ("OneOvenFourFirings") asserts 4 bakes with a 5th batch parked, but
        /// 4 full bread batches need 4 × (120 prep + 60 bake) = 720 hands-on
        /// minutes — the 600 budget only ever fit 3, so the width's own test
        /// could never pass (written, not run). 840 covers 5 preps + 4 bakes
        /// (840 exactly), letting the oven's 4 firings bind as the test
        /// intends. A 14-hour bake day (dough at dawn, bake through the
        /// afternoon) is the historical frontier rhythm.
        /// </summary>
        public const int BakerMinutesPerDay = 840;

        private readonly string businessInstanceId;
        private readonly BakeryFlourStock flourStock = new BakeryFlourStock();

        /// <summary>D1D: the oven-firebox fuel store (cordwood, provenance-tracked).</summary>
        private readonly BakeryFuelStock fuelStock = new BakeryFuelStock();

        private readonly List<BakeryOven> ovens = new List<BakeryOven>();
        private readonly List<BakeryDoughBatch> batches = new List<BakeryDoughBatch>();
        private readonly List<BakeryBreadLot> breadShelf = new List<BakeryBreadLot>();
        private readonly List<BakerySaleRecord> sales = new List<BakerySaleRecord>();
        private readonly List<BakeryWasteRecord> waste = new List<BakeryWasteRecord>();
        private readonly List<string> wasteNotedLotIds = new List<string>();

        /// <summary>D1D: wholesale supply agreements (Canon §7.3E).</summary>
        private readonly List<BakeryWholesaleAccount> wholesaleAccounts = new List<BakeryWholesaleAccount>();

        /// <summary>D1D: fulfilled wholesale deliveries (the goods side of each account fill).</summary>
        private readonly List<BakeryWholesaleDeliveryRecord> wholesaleDeliveries = new List<BakeryWholesaleDeliveryRecord>();

        /// <summary>D1D: delivery runs — wholesale loads moving to buyers (Canon §7.3E, Tech X §9.3).</summary>
        private readonly List<BakeryDeliveryRun> deliveryRuns = new List<BakeryDeliveryRun>();

        private BakeryPriceSchedule prices = new BakeryPriceSchedule();

        /// <summary>
        /// D1D: proofing-rack slots. Canon equipment profile lists the
        /// proofing rack ("expanded racks" are researched expansion gear) but
        /// gives no count — default is calibration, settable per shop.
        /// </summary>
        private int proofingRackSlots = BakeryBreadCatalog.DefaultProofingRackSlots;

        /// <summary>
        /// D1D: who scarce supply protects (Canon §7.3E: "the player can
        /// choose"). Default ProtectWholesaleContracts is TUNING — canon gives
        /// no default.
        /// </summary>
        private BakerySupplyProtectionPolicy supplyProtection = BakerySupplyProtectionPolicy.ProtectWholesaleContracts;

        private int batchSeq;
        private int saleSeq;
        private int proofSeq;
        private int deliverySeq;
        private int runSeq;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public BakeryFlourStock FlourStock => flourStock;

        /// <summary>D1D: the oven-firebox fuel store.</summary>
        public BakeryFuelStock FuelStock => fuelStock;

        public IReadOnlyList<BakeryOven> Ovens => ovens;
        public IReadOnlyList<BakeryDoughBatch> Batches => batches;
        public IReadOnlyList<BakeryBreadLot> BreadShelf => breadShelf;
        public IReadOnlyList<BakerySaleRecord> Sales => sales;
        public IReadOnlyList<BakeryWasteRecord> Waste => waste;
        public BakeryPriceSchedule Prices => prices;

        /// <summary>D1D: wholesale supply agreements on the books.</summary>
        public IReadOnlyList<BakeryWholesaleAccount> WholesaleAccounts => wholesaleAccounts;

        /// <summary>D1D: fulfilled wholesale deliveries.</summary>
        public IReadOnlyList<BakeryWholesaleDeliveryRecord> WholesaleDeliveries => wholesaleDeliveries;

        /// <summary>D1D: delivery runs (staged, in transit, delivered, awaiting pickup).</summary>
        public IReadOnlyList<BakeryDeliveryRun> DeliveryRuns => deliveryRuns;

        /// <summary>D1D: proofing-rack slots gating the daily proof schedule.</summary>
        public int ProofingRackSlots => proofingRackSlots;

        /// <summary>D1D: the owner's scarcity policy (Canon §7.3E).</summary>
        public BakerySupplyProtectionPolicy SupplyProtection
        {
            get => supplyProtection;
            set => supplyProtection = value == BakerySupplyProtectionPolicy.Unspecified
                ? BakerySupplyProtectionPolicy.ProtectWholesaleContracts
                : value;
        }

        /// <summary>
        /// D1D: sets proofing-rack capacity (canon "expanded racks" upgrade —
        /// a bigger rack proofs more batches per day). Non-positive values are
        /// refused loudly; the old capacity stands.
        /// </summary>
        public string SetProofingRackSlots(int slots, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (slots <= 0)
            {
                string message = $"BakeryShopRuntime: proofing rack needs a positive slot count — {proofingRackSlots} slot(s) stand.";
                diag.Add(message);
                return message;
            }

            proofingRackSlots = slots;
            diag.Add($"BakeryShopRuntime: proofing rack set to {slots} slot(s).");
            return null;
        }

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
        /// 2. Prep cascade: planned batches dispense flour and notions into
        ///    custody (loud refusal on shortfall — the batch stays planned),
        ///    then enter proofing on the rack.
        /// 3. Proof pass: the day's proof budget (rack slots ×
        ///    ProofMinutesPerSlotPerDay) is allocated FIFO; proofed batches
        ///    attempt the bake — one firing on a ready oven, cordwood for the
        ///    firebox, baker labor — while budgets last.
        /// D1D: proofing sits between prep and bake (Canon Part III §3.1) and
        /// every firing burns fuel (canon "Oven/firebox"). Each WorkDay call
        /// is one day: firing budgets reset at its start.
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

            // Prep cascade: planned → prepped (custody) → proofing (rack).
            var snapshot = new List<BakeryDoughBatch>(batches);
            foreach (var batch in snapshot)
            {
                if (batch == null || !batch.IsActive) continue;
                PrepBatchToProofing(batch, dayIndex, ref laborRemaining, diag, idRegistry);
            }

            // Proof pass then bake pass.
            return RunProofAndBakePass(dayIndex, ref laborRemaining, ovenFiringsLeft, diag, idRegistry);
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
        /// D1D: moves one batch from Planned through prep into Proofing.
        /// Prep dispenses flour and notions into the batch's custody (loud
        /// refusal on shortfall — the batch stays planned); proofing claims
        /// the batch's place in the proof queue with its product's proof
        /// minutes. Prepped batches loaded from older saves enter proofing
        /// without re-dispensing — their custody is already held.
        /// </summary>
        private void PrepBatchToProofing(
            BakeryDoughBatch batch, int dayIndex, ref int laborRemaining,
            List<string> diag, EntityIdRegistry idRegistry)
        {
            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(batch.ProductId);
            if (string.IsNullOrEmpty(spec.ProductId))
            {
                diag.Add($"BakeryShopRuntime: batch {batch.BatchId} names unknown product '{batch.ProductId}' — stays parked, never guessed.");
                return;
            }

            if (batch.Stage == BakeryBatchStage.Planned)
            {
                // Prep: flour and notions into the batch's custody, FIFO lots.
                var flourLines = flourStock.TryDispenseUnits(
                    BakeryBreadCatalog.FlourItemId, spec.FlourUnitsPerBatch, dayIndex, diag);
                if (flourLines == null)
                {
                    diag.Add($"BakeryShopRuntime: batch {batch.BatchId} ({spec.DisplayName}) refused — no flour on hand. Stays planned.");
                    return;
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
                    return;
                }

                if (!SpendLabor(ref laborRemaining, spec.PrepMinutes, diag, batch, "prepping"))
                {
                    ReturnCustodyToBin(batch, flourLines, dayIndex, diag, idRegistry);
                    ReturnCustodyToBin(batch, notionLines, dayIndex, diag, idRegistry);
                    diag.Add($"BakeryShopRuntime: batch {batch.BatchId} prep needs {spec.PrepMinutes}m — stays planned for tomorrow.");
                    return;
                }

                batch.FlourInCustody.AddRange(flourLines);
                batch.NotionsUsed.AddRange(notionLines);
                SetStage(batch, BakeryBatchStage.Prepped, dayIndex,
                    $"prepped ({spec.PrepMinutes}m, {spec.FlourUnitsPerBatch} lb flour from {flourLines.Count} lot(s))");
            }

            if (batch.Stage == BakeryBatchStage.Prepped)
            {
                batch.ProofMinutesRemaining = spec.ProofMinutes;
                batch.ProofOrder = proofSeq++;
                SetStage(batch, BakeryBatchStage.Proofing, dayIndex,
                    $"on the proofing rack ({spec.ProofMinutes}m to proof)");
            }
        }

        /// <summary>
        /// D1D: the proof pass and the bake pass. Proof budget (rack slots ×
        /// per-slot minutes) is allocated FIFO by proof-entry order; proofed
        /// batches then attempt the bake — one firing on a ready oven,
        /// cordwood for the firebox, baker labor. Returns the number of
        /// batches baked.
        /// </summary>
        private int RunProofAndBakePass(
            int dayIndex, ref int laborRemaining,
            Dictionary<int, int> ovenFiringsLeft, List<string> diag,
            EntityIdRegistry idRegistry)
        {
            var proofing = new List<BakeryDoughBatch>();
            foreach (var batch in batches)
            {
                if (batch != null && batch.Stage == BakeryBatchStage.Proofing) proofing.Add(batch);
            }

            List<BakeryDoughBatch> stillWaiting;
            BakeryProofScheduler.AllocateProofCredit(proofing, proofingRackSlots, dayIndex, diag, out stillWaiting);

            int baked = 0;
            bool fuelShortfallNoted = false;
            bool firingsExhaustedNoted = false;
            bool ovenAvailable = ovenFiringsLeft.Count > 0;
            foreach (var batch in proofing)
            {
                if (batch.ProofMinutesRemaining > 0) continue;
                if (!ovenAvailable) break; // "no ready bake oven" already noted above — never "fully booked" with no oven at all.

                BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(batch.ProductId);
                if (string.IsNullOrEmpty(spec.ProductId))
                {
                    diag.Add($"BakeryShopRuntime: batch {batch.BatchId} names unknown product '{batch.ProductId}' — stays parked, never guessed.");
                    continue;
                }

                int ovenIndex = ClaimFiring(ovenFiringsLeft);
                if (ovenIndex < 0)
                {
                    if (!firingsExhaustedNoted)
                    {
                        firingsExhaustedNoted = true;
                        diag.Add($"BakeryShopRuntime: batch {batch.BatchId} ({spec.DisplayName}) waits on an oven firing — all ready ovens are fully booked today. Stays proofed.");
                    }

                    break;
                }

                if (!SpendLabor(ref laborRemaining, spec.BakeMinutes, diag, batch, "baking"))
                {
                    ovenFiringsLeft[ovenIndex]++;
                    diag.Add($"BakeryShopRuntime: batch {batch.BatchId} bake needs {spec.BakeMinutes}m — stays proofed for tomorrow.");
                    break;
                }

                // D1D: the firebox burns fuel. No cordwood, no firing — the
                // batch stays proofed, loudly. Labor not spent is returned.
                var fuelLines = fuelStock.TryDispenseUnits(
                    BakeryBreadCatalog.FuelItemId, BakeryBreadCatalog.FuelUnitsPerFiring, dayIndex, diag);
                if (fuelLines == null)
                {
                    ovenFiringsLeft[ovenIndex]++;
                    laborRemaining += spec.BakeMinutes;
                    if (!fuelShortfallNoted)
                    {
                        fuelShortfallNoted = true;
                        diag.Add($"BakeryShopRuntime: no oven fuel on hand — batch {batch.BatchId} ({spec.DisplayName}) stays proofed. " +
                            "The firebox burns cordwood; reorder via fuel dealers or import orders.");
                    }

                    break;
                }

                BakeBatch(batch, spec, ovenIndex, dayIndex, diag, idRegistry, fuelLines);
                baked++;
            }

            return baked;
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

        /// <summary>
        /// Bakes a proofed batch: one firing → one finished-goods lot with
        /// full input provenance (flour, notions, and the firing's fuel).
        /// </summary>
        private void BakeBatch(
            BakeryDoughBatch batch, BakeryProductSpec spec, int ovenIndex, int dayIndex,
            List<string> diag, EntityIdRegistry idRegistry,
            List<BakeryFuelDispenseLine> fuelLines)
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

            // D1D: the firing's fuel rides along — the firebox's cordwood is
            // part of the loaf's upstream chain.
            if (fuelLines != null)
            {
                foreach (var line in fuelLines)
                {
                    if (line != null) lot.InputProvenance.Add($"fired with {line.UnitsTaken}× {line.FuelName} from {line.ProvenanceChain}");
                }
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

            // D1D: the owner's scarcity policy (Canon §7.3E) caps what the
            // retail counter may sell when wholesale accounts hold due
            // orders. With no accounts on the books this changes nothing.
            int retailAllowance = RetailAllowanceFor(productId, dayIndex);
            int remaining = Math.Min(unitsRequested, Math.Max(0, retailAllowance));
            if (remaining < unitsRequested)
            {
                diag.Add($"BakeryShopRuntime: retail counter held to {remaining}/{unitsRequested} {productId} — " +
                    $"supply protection ({supplyProtection}) reserves shelf stock for due wholesale accounts.");
            }

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

        #region D1D Wholesale accounts (Canon §7.3E)

        /// <summary>
        /// D1D: books a wholesale supply agreement with a repeat buyer
        /// (boarding house, hotel, restaurant, store, saloon, camp — Canon
        /// §7.3E). Returns a rejection string, or null on success.
        /// </summary>
        public string AddWholesaleAccount(BakeryWholesaleAccount account, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (account == null) return "BakeryShopRuntime: null wholesale account refused.";
            if (string.IsNullOrWhiteSpace(account.BuyerBusinessId))
                return "BakeryShopRuntime: wholesale account refused — the buying business must be named.";
            if (!BakeryBreadCatalog.IsKnownProduct(account.ProductId))
                return $"BakeryShopRuntime: wholesale account refused — unknown product '{account.ProductId}'.";
            if (account.UnitsPerDelivery <= 0)
                return "BakeryShopRuntime: wholesale account refused — units per delivery must be positive.";
            if (account.DeliveryCadenceDays <= 0) account.DeliveryCadenceDays = 1;
            if (account.PricePerUnitCents < 0) account.PricePerUnitCents = 0;
            if (string.IsNullOrWhiteSpace(account.AccountId))
                account.AccountId = $"{businessInstanceId}-wholesale-{wholesaleAccounts.Count}";
            if (account.Freshness == BakeryWholesaleFreshness.Unspecified)
                account.Freshness = BakeryWholesaleFreshness.FreshOrDayOld;

            wholesaleAccounts.Add(account);
            diag.Add($"BakeryShopRuntime: wholesale account {account.AccountId} — {account.UnitsPerDelivery}× {account.ProductId} " +
                $"every {account.DeliveryCadenceDays} day(s) to {account.BuyerBusinessId} ({account.BuyerType}) " +
                $"at {account.PricePerUnitCents}¢/unit, {account.Freshness}.");
            return null;
        }

        /// <summary>
        /// D1D: turns due wholesale accounts into planned dough batches
        /// (Canon §7.3E: large recurring buyers improve throughput visibility).
        /// Accounts due within the planning lead window are covered from the
        /// existing pipeline first — only the shortfall is planned. Returns
        /// the planned batch ids. Planning never bakes; <see cref="WorkDay"/>
        /// executes.
        /// </summary>
        public List<string> PlanWholesaleBatches(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var planned = new List<string>();
            foreach (var account in wholesaleAccounts)
            {
                if (account == null || !account.IsActive) continue;
                if (account.NextDeliveryDayIndex > dayIndex + BakeryBreadCatalog.WholesalePlanningLeadDays) continue;

                BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(account.ProductId);
                if (string.IsNullOrEmpty(spec.ProductId)) continue;

                int covered = 0;
                foreach (var batch in batches)
                {
                    if (batch != null && batch.IsActive
                        && string.Equals(batch.ProductId, account.ProductId, StringComparison.Ordinal))
                    {
                        covered += spec.YieldUnitsPerBatch;
                    }
                }

                int needed = Math.Max(0, account.UnitsPerDelivery) - covered;
                int toPlan = (needed + spec.YieldUnitsPerBatch - 1) / spec.YieldUnitsPerBatch;
                if (toPlan <= 0)
                {
                    diag.Add($"BakeryShopRuntime: wholesale account {account.AccountId} already covered ({covered} units in the pipeline).");
                    continue;
                }

                var ids = PlanBatch(account.ProductId, toPlan, dayIndex, diag);
                if (ids == null) continue; // refusal already noted loudly by PlanBatch
                planned.AddRange(ids);
                diag.Add($"BakeryShopRuntime: planned {toPlan} batch(es) for wholesale account {account.AccountId} " +
                    $"(delivery day {account.NextDeliveryDayIndex}, {account.BuyerBusinessId}).");
            }

            return planned;
        }

        /// <summary>
        /// D1D: fills one wholesale account's due delivery from the shelf.
        /// Freshest lots ship first (wholesale buyers get the best bread; the
        /// retail counter sells day-old first per W2A, so the two outlets
        /// partition the shelf). Stale lots never ship. Shortfalls are loud,
        /// never faked — the record carries FullyCovered = false. Advances the
        /// account to its next delivery day. Returns the delivery record.
        /// </summary>
        public BakeryWholesaleDeliveryRecord FulfillWholesale(
            string accountId, int dayIndex, List<string> diag, EntityIdRegistry idRegistry)
        {
            diag = diag ?? diagnostics;
            BakeryWholesaleAccount account = null;
            foreach (var candidate in wholesaleAccounts)
            {
                if (candidate != null && string.Equals(candidate.AccountId, accountId, StringComparison.Ordinal))
                {
                    account = candidate;
                    break;
                }
            }

            if (account == null || !account.IsActive)
            {
                diag.Add($"BakeryShopRuntime: wholesale fulfillment refused — no active account '{accountId}'.");
                return null;
            }

            AgeShelf(dayIndex, diag);

            bool allowDayOld = account.Freshness != BakeryWholesaleFreshness.FreshOnly;
            var sellable = new List<BakeryBreadLot>();
            foreach (var lot in breadShelf)
            {
                if (lot == null || !lot.CanSell) continue;
                if (!string.Equals(lot.ProductId, account.ProductId, StringComparison.Ordinal)) continue;
                if (lot.Condition == BakeryGoodsCondition.DayOld && !allowDayOld) continue;
                sellable.Add(lot);
            }

            // Freshest first, then newest-baked within a condition.
            sellable.Sort((a, b) =>
            {
                int byCondition = a.Condition.CompareTo(b.Condition);
                return byCondition != 0
                    ? byCondition
                    : b.BakedDayIndex.CompareTo(a.BakedDayIndex);
            });

            var record = new BakeryWholesaleDeliveryRecord
            {
                DeliveryId = $"{businessInstanceId}-wholesale-delivery-{deliverySeq++}",
                AccountId = account.AccountId,
                BuyerBusinessId = account.BuyerBusinessId,
                BuyerType = account.BuyerType,
                ProductId = account.ProductId,
                PricePerUnitCents = Math.Max(0, account.PricePerUnitCents),
                DayIndex = dayIndex,
            };

            int remaining = Math.Max(0, account.UnitsPerDelivery);
            int freshTaken = 0;
            int dayOldTaken = 0;
            foreach (var lot in sellable)
            {
                if (remaining <= 0) break;
                int taken = lot.TakeUnits(remaining);
                if (taken <= 0) continue;
                remaining -= taken;

                record.SellerLotIds.Add(lot.LotId.ToString());
                record.UnitsByLot.Add(taken);
                record.ConditionByLot.Add(lot.Condition);
                record.BakedDayByLot.Add(lot.BakedDayIndex);
                if (lot.InputProvenance != null) record.ProvenanceLines.AddRange(lot.InputProvenance);
                if (lot.Condition == BakeryGoodsCondition.Fresh) freshTaken += taken;
                else dayOldTaken += taken;
            }

            record.Units = Math.Max(0, account.UnitsPerDelivery) - remaining;
            record.FullyCovered = remaining <= 0;
            wholesaleDeliveries.Add(record);
            account.NextDeliveryDayIndex += Math.Max(1, account.DeliveryCadenceDays);

            diag.Add($"BakeryShopRuntime: wholesale delivery {record.DeliveryId} — {record.Units}/{account.UnitsPerDelivery} {account.ProductId} " +
                $"to {account.BuyerBusinessId} ({freshTaken} fresh, {dayOldTaken} day-old)" +
                (record.FullyCovered ? "." : $" — SHORT {remaining} units, loudly (Canon §9.2: finite supply)."));
            return record;
        }

        /// <summary>
        /// D1D: units of a product currently committed to due wholesale
        /// accounts (active, delivery day reached). The scarcity policy reads
        /// this to protect contracts against the retail counter.
        /// </summary>
        private int WholesaleUnitsDue(string productId, int dayIndex)
        {
            int due = 0;
            foreach (var account in wholesaleAccounts)
            {
                if (account == null || !account.IsActive) continue;
                if (!string.Equals(account.ProductId, productId, StringComparison.Ordinal)) continue;
                if (account.NextDeliveryDayIndex <= dayIndex) due += Math.Max(0, account.UnitsPerDelivery);
            }

            return due;
        }

        /// <summary>
        /// D1D: what the retail counter may sell of a product today under the
        /// owner's scarcity policy (Canon §7.3E).
        /// </summary>
        private int RetailAllowanceFor(string productId, int dayIndex)
        {
            int totalSellable = 0;
            foreach (var lot in breadShelf)
            {
                if (lot != null && lot.CanSell
                    && string.Equals(lot.ProductId, productId, StringComparison.Ordinal))
                {
                    totalSellable += lot.UnitCount;
                }
            }

            if (supplyProtection == BakerySupplyProtectionPolicy.ProtectRetailCounter)
                return totalSellable;

            int committed = WholesaleUnitsDue(productId, dayIndex);
            if (supplyProtection == BakerySupplyProtectionPolicy.ProportionalShare)
            {
                int reference = Math.Max(1, BakeryBreadCatalog.RetailReferenceDemandUnits);
                return Math.Min(totalSellable, totalSellable * reference / (reference + Math.Max(0, committed)));
            }

            // ProtectWholesaleContracts (the default): due accounts first.
            return Math.Max(0, totalSellable - Math.Max(0, committed));
        }

        #endregion

        #region D1D Delivery runs (Canon §7.3E, Tech X §9.3)

        /// <summary>
        /// D1D: stages a delivery run for a fulfilled wholesale delivery.
        /// With a wagon and team the run is staged for departure (real
        /// operating cost: driver minutes + haul cost); without transport the
        /// run waits for buyer pickup — bread never teleports (Tech X §9.3).
        /// </summary>
        public BakeryDeliveryRun StageDeliveryRun(
            BakeryWholesaleDeliveryRecord record,
            BakeryHaulingTerms terms,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (record == null)
            {
                diag.Add("BakeryShopRuntime: no wholesale delivery to stage — run refused.");
                return null;
            }

            terms = terms ?? new BakeryHaulingTerms();

            var run = new BakeryDeliveryRun
            {
                RunId = $"{businessInstanceId}-delivery-run-{runSeq++}",
                WholesaleDeliveryId = record.DeliveryId,
                BuyerBusinessId = record.BuyerBusinessId,
                BuyerType = record.BuyerType,
                ProductId = record.ProductId,
            };

            for (int i = 0; i < record.SellerLotIds.Count; i++)
            {
                run.CargoLines.Add(new BakeryDeliveryCargoLine
                {
                    SellerLotId = record.SellerLotIds[i],
                    ProductId = record.ProductId,
                    Units = i < record.UnitsByLot.Count ? Math.Max(0, record.UnitsByLot[i]) : 0,
                    ConditionAtLoad = i < record.ConditionByLot.Count
                        ? record.ConditionByLot[i]
                        : BakeryGoodsCondition.Unspecified,
                    BakedDayIndex = i < record.BakedDayByLot.Count ? record.BakedDayByLot[i] : dayIndex,
                    ProvenanceLines = new List<string>(record.ProvenanceLines ?? new List<string>()),
                });
            }

            if (!terms.CanDeliver)
            {
                run.Status = BakeryDeliveryRunStatus.AwaitingBuyerPickup;
                run.Hauling = LocalRecurringOrderHaulingResponsibility.Buyer;
                diag.Add($"BakeryShopRuntime: delivery run {run.RunId} staged for buyer pickup — no bakery wagon/team " +
                    "(Canon §7.3E: delivery only when labor and transport exist). Bread never teleports.");
            }
            else
            {
                int miles = terms.MilesFor(record.BuyerBusinessId);
                run.Status = BakeryDeliveryRunStatus.Staged;
                run.WagonId = terms.WagonId;
                run.TeamId = terms.TeamId;
                run.TransitDays = terms.TransitDaysFor(record.BuyerBusinessId);
                run.DriverLaborMinutes = miles * Math.Max(0, terms.DriverMinutesPerMile);
                run.HaulCostCents = miles * Math.Max(0, terms.HaulCostCentsPerMile);
                run.Hauling = LocalRecurringOrderHaulingResponsibility.Seller;
                diag.Add($"BakeryShopRuntime: delivery run {run.RunId} staged — {run.TotalUnits} {record.ProductId} " +
                    $"to {record.BuyerBusinessId} ({miles} mi, {run.TransitDays} transit day(s), " +
                    $"{run.DriverLaborMinutes}m driver labor, {run.HaulCostCents}¢ haul cost).");
            }

            deliveryRuns.Add(run);
            return run;
        }

        /// <summary>
        /// D1D: departs every staged run. Arrival day = depart day + transit
        /// days (Tech X §9.3: goods travel, they do not teleport).
        /// </summary>
        public void DepartDeliveryRuns(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (var run in deliveryRuns)
            {
                if (run == null || run.Status != BakeryDeliveryRunStatus.Staged) continue;
                run.Status = BakeryDeliveryRunStatus.InTransit;
                run.DepartDayIndex = dayIndex;
                run.ArrivalDayIndex = dayIndex + Math.Max(0, run.TransitDays);
                diag.Add($"BakeryShopRuntime: delivery run {run.RunId} departed (day {dayIndex}) → {run.BuyerBusinessId}, arrives day {run.ArrivalDayIndex}.");
            }
        }

        /// <summary>
        /// D1D: arrives every in-transit run due on or before the given day.
        /// Cargo is aged to the arrival day — bread that went stale in transit
        /// is written off as waste with provenance retained, never delivered.
        /// Returns the delivered runs so the caller can feed each cargo line's
        /// lot snapshot to the buyer's pantry intake
        /// (e.g. BoardingHouseFoodSupply.ReceiveBakeryBreadLot).
        /// </summary>
        public List<BakeryDeliveryRun> ArriveDeliveryRuns(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var arrived = new List<BakeryDeliveryRun>();
            foreach (var run in deliveryRuns)
            {
                if (run == null) continue;
                if (run.Status != BakeryDeliveryRunStatus.InTransit) continue;
                if (run.ArrivalDayIndex > dayIndex) continue;

                if (run.CargoLines != null)
                {
                    foreach (var line in run.CargoLines)
                    {
                        if (line == null || line.Units <= 0) continue;
                        if (line.ConditionOnArrival(dayIndex) == BakeryGoodsCondition.Stale)
                        {
                            var chains = new List<string>
                            {
                                $"seller lot {line.SellerLotId} (bakery {businessInstanceId})",
                            };
                            if (line.ProvenanceLines != null) chains.AddRange(line.ProvenanceLines);
                            waste.Add(new BakeryWasteRecord
                            {
                                ProductId = line.ProductId,
                                Units = line.Units,
                                BakedDayIndex = line.BakedDayIndex,
                                WastedDayIndex = dayIndex,
                                ProvenanceChain = string.Join(" | ", chains),
                            });
                            diag.Add($"BakeryShopRuntime: delivery run {run.RunId} — {line.Units} {line.ProductId} went stale in transit " +
                                $"(baked day {line.BakedDayIndex}, arrived day {dayIndex}). Written off as waste; stale bread is never delivered.");
                            line.Units = 0;
                        }
                    }
                }

                run.Status = BakeryDeliveryRunStatus.Delivered;
                arrived.Add(run);
                diag.Add($"BakeryShopRuntime: delivery run {run.RunId} delivered to {run.BuyerBusinessId} (day {dayIndex}, {run.TotalUnits} units).");
            }

            return arrived;
        }

        /// <summary>
        /// D1D: the buyer collects a run that was waiting for pickup. The run
        /// delivers with same-day arrival aging.
        /// </summary>
        public string MarkPickedUp(string runId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (var run in deliveryRuns)
            {
                if (run == null || !string.Equals(run.RunId, runId, StringComparison.Ordinal)) continue;
                if (run.Status != BakeryDeliveryRunStatus.AwaitingBuyerPickup)
                    return $"BakeryShopRuntime: run {runId} is {run.Status}, not awaiting pickup.";
                run.Status = BakeryDeliveryRunStatus.Delivered;
                run.DepartDayIndex = dayIndex;
                run.ArrivalDayIndex = dayIndex;
                diag.Add($"BakeryShopRuntime: run {runId} picked up by {run.BuyerBusinessId} (day {dayIndex}, {run.TotalUnits} units).");
                return null;
            }

            return $"BakeryShopRuntime: no delivery run '{runId}'.";
        }

        /// <summary>
        /// D1D: finds a bread lot on the shelf by its string lot id (for
        /// orchestrators resolving transfer records).
        /// </summary>
        public BakeryBreadLot FindBreadLot(string lotIdString)
        {
            if (string.IsNullOrEmpty(lotIdString)) return null;
            foreach (var lot in breadShelf)
            {
                if (lot != null && string.Equals(lot.LotId.ToString(), lotIdString, StringComparison.Ordinal))
                    return lot;
            }

            return null;
        }

        #endregion

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
                $"{flourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId)} lb flour, " +
                $"{fuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId)} fuel units, " +
                $"{proofingRackSlots} proofing slot(s), {wholesaleAccounts.Count} wholesale account(s), " +
                $"{deliveryRuns.Count} delivery run(s).";
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

            /// <summary>D1D: the oven-firebox fuel store.</summary>
            public BakeryFuelStock.BakeryFuelStockSaveDto FuelStock = new BakeryFuelStock.BakeryFuelStockSaveDto();

            public List<BakeryOven> Ovens = new List<BakeryOven>();
            public List<BakeryDoughBatch> Batches = new List<BakeryDoughBatch>();
            public List<BakeryBreadLot> BreadShelf = new List<BakeryBreadLot>();
            public List<BakerySaleRecord> Sales = new List<BakerySaleRecord>();
            public List<BakeryWasteRecord> Waste = new List<BakeryWasteRecord>();
            public List<string> WasteNotedLotIds = new List<string>();

            /// <summary>D1D: wholesale supply agreements.</summary>
            public List<BakeryWholesaleAccount> WholesaleAccounts = new List<BakeryWholesaleAccount>();

            /// <summary>D1D: fulfilled wholesale deliveries.</summary>
            public List<BakeryWholesaleDeliveryRecord> WholesaleDeliveries = new List<BakeryWholesaleDeliveryRecord>();

            /// <summary>D1D: delivery runs.</summary>
            public List<BakeryDeliveryRun> DeliveryRuns = new List<BakeryDeliveryRun>();

            /// <summary>D1D: proofing-rack slots.</summary>
            public int ProofingRackSlots = BakeryBreadCatalog.DefaultProofingRackSlots;

            /// <summary>D1D: the owner's scarcity policy.</summary>
            public BakerySupplyProtectionPolicy SupplyProtection = BakerySupplyProtectionPolicy.ProtectWholesaleContracts;

            public int BatchSeq;
            public int SaleSeq;

            /// <summary>D1D: proof-entry order sequence.</summary>
            public int ProofSeq;

            /// <summary>D1D: wholesale delivery id sequence.</summary>
            public int DeliverySeq;

            /// <summary>D1D: delivery run id sequence.</summary>
            public int RunSeq;
        }

        public BakeryShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new BakeryShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                Prices = prices,
                FlourStock = flourStock.CaptureSaveDto(),
                FuelStock = fuelStock.CaptureSaveDto(),
                ProofingRackSlots = proofingRackSlots,
                SupplyProtection = supplyProtection,
                BatchSeq = batchSeq,
                SaleSeq = saleSeq,
                ProofSeq = proofSeq,
                DeliverySeq = deliverySeq,
                RunSeq = runSeq,
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
            dto.WholesaleAccounts.AddRange(wholesaleAccounts);
            dto.WholesaleDeliveries.AddRange(wholesaleDeliveries);
            dto.DeliveryRuns.AddRange(deliveryRuns);
            return dto;
        }

        public void LoadFromSaveDto(BakeryShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            flourStock.LoadFromSaveDto(dto.FlourStock);
            fuelStock.LoadFromSaveDto(dto.FuelStock);
            if (dto.Prices != null) prices = dto.Prices;
            if (dto.ProofingRackSlots > 0) proofingRackSlots = dto.ProofingRackSlots;
            if (dto.SupplyProtection != BakerySupplyProtectionPolicy.Unspecified)
                supplyProtection = dto.SupplyProtection;
            batchSeq = Math.Max(0, dto.BatchSeq);
            saleSeq = Math.Max(0, dto.SaleSeq);
            proofSeq = Math.Max(0, dto.ProofSeq);
            deliverySeq = Math.Max(0, dto.DeliverySeq);
            runSeq = Math.Max(0, dto.RunSeq);
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

            wholesaleAccounts.Clear();
            if (dto.WholesaleAccounts != null)
            {
                foreach (var account in dto.WholesaleAccounts)
                {
                    if (account != null) wholesaleAccounts.Add(account);
                }
            }

            wholesaleDeliveries.Clear();
            if (dto.WholesaleDeliveries != null)
            {
                foreach (var record in dto.WholesaleDeliveries)
                {
                    if (record != null) wholesaleDeliveries.Add(record);
                }
            }

            deliveryRuns.Clear();
            if (dto.DeliveryRuns != null)
            {
                foreach (var run in dto.DeliveryRuns)
                {
                    if (run != null) deliveryRuns.Add(run);
                }
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
