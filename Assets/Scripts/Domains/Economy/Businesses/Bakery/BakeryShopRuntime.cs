using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Logistics;
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

        /// <summary>
        /// Priority spoilage integration: the Slice 5 perishable
        /// condition state attached to this lot (additive; old saves
        /// load with HasPerishableCondition false, which means "never
        /// attached", never a default Fresh). Created at bake, aged by
        /// AgeShelf through the authored AgeToDay rule, split by sale
        /// and wholesale fulfillment. The save DTO carries the bread
        /// shelf list directly, so the state round-trips with the lot.
        /// </summary>
        public PerishableLotState.SaveDto PerishableCondition;

        /// <summary>Whether <see cref="PerishableCondition"/> was attached at a creation site.</summary>
        public bool HasPerishableCondition;

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
            // Priority spoilage integration: keep the attached state's
            // units honest — the lot now holds UnitCount. The taken
            // part's own state is captured by the caller BEFORE this
            // call (SellBread / FulfillWholesale split first); a caller
            // that takes directly (the Review2 wholesale driver) leaves
            // the taken part unrecorded but the remaining state stays
            // correct, and this sync is idempotent with the pre-split.
            if (taken > 0 && HasPerishableCondition)
            {
                PerishableCondition = PerishableLotState.FromSaveDto(PerishableCondition)
                    .WithUnits(UnitCount).CaptureSaveDto();
            }
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

        /// <summary>
        /// Priority spoilage integration: the condition state of the
        /// units taken from this lot, at the moment of sale (additive;
        /// absent when the lot carried no state). The buyer carries
        /// this part forward; the remainder stays on the lot.
        /// </summary>
        public PerishableLotState.SaveDto PerishableCondition;

        /// <summary>Whether <see cref="PerishableCondition"/> was attached at the sale.</summary>
        public bool HasPerishableCondition;

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

        /// <summary>
        /// Lane 10a (H-3): the persisted sales-channel decision history —
        /// every policy resolution/change and every account add/pause, with
        /// who/what/when/why and the observed inputs at decision time.
        /// The Incident Investigator and the Business Operations Monitor
        /// read this list instead of a bare policy value.
        /// </summary>
        private readonly List<BakeryChannelDecision> channelDecisions = new List<BakeryChannelDecision>();

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
        /// choose"). Default ProtectRetailCounter is an OWNER DECISION
        /// taken in Lane 9a (§19 retail-first, Nov 20, 2026): canon gives
        /// no default, so the code default IS the decision — the retail
        /// counter sells freely and wholesale accounts get the remainder.
        /// The field initializer and the save-DTO default below remain
        /// only as deserialization fallbacks for old saves, never as a
        /// decision; Lane 10a (H-2) adds the config-authored initial
        /// policy resolved once as a recorded decision.
        /// </summary>
        private BakerySupplyProtectionPolicy supplyProtection = BakerySupplyProtectionPolicy.ProtectRetailCounter;

        private int batchSeq;
        private int saleSeq;
        private int proofSeq;
        private int deliverySeq;
        private int runSeq;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId ?? string.Empty;

        /// <summary>
        /// Priority 9 Slice F3 (P9 plan section 4 Slice F3): the custody
        /// transfer log this runtime records flour/notions dispenses and
        /// bakes into, when set. Null (the default) means no recording —
        /// today behaviour. The log is an audit trail only: no flow reads
        /// it, and a logging failure never fails a bake.
        /// </summary>
        public CustodyTransferLog CustodyLog { get; set; }

        /// <summary>
        /// W6 (W-5, Obj 9 design): the owner-person link — the SEAM for
        /// baker person-time booking. Resolved from the business owner, the
        /// same pattern as the freight owner resolution in SimulationDrivers
        /// (bakery.Owner.PersonId). -1 = unbound: no owner was bound, or the
        /// bound owner has no person (e.g. the Player). Lane 10a (H-6):
        /// the fail-closed worker gate DOES read this link and refuses the
        /// bake when it is unbound — the old "Nothing reads this yet"
        /// comment was false and is removed. The link is re-bound at
        /// bootstrap, not saved — the save DTO does not carry it, so a
        /// restored runtime re-binds from the live business owner.
        /// </summary>
        public int OwnerPersonId { get; private set; } = -1;

        /// <summary>
        /// W6: binds the runtime to its owner person. Returns null on
        /// success, else a loud refusal (null owner, or an owner with no
        /// person id) — the link stays unbound (-1) and the caller must not
        /// book person-time against it. No crash, no silent default person.
        /// Re-binding to a different owner person replaces the link (the
        /// bootstrap re-bind after a restore); binding the same person
        /// again is a no-op success.
        /// </summary>
        public string BindOwnerPerson(BusinessOwnerIdentity owner, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (owner == null)
            {
                string message = $"BakeryShopRuntime '{businessInstanceId}': no business owner supplied — owner-person link stays unbound; person-time booking is refused until the owner is bound.";
                diag.Add(message);
                return message;
            }
            if (owner.PersonId < 0)
            {
                string message = $"BakeryShopRuntime '{businessInstanceId}': owner '{owner.DisplayName}' ({owner.OwnerKind}) has no person id — owner-person link stays unbound; person-time booking is refused until an NPC owner is bound.";
                diag.Add(message);
                return message;
            }
            if (OwnerPersonId == owner.PersonId) return null;
            OwnerPersonId = owner.PersonId;
            diag.Add($"BakeryShopRuntime '{businessInstanceId}': owner-person link bound to P{owner.PersonId} ({owner.DisplayName}).");
            return null;
        }

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

        /// <summary>Lane 10a (H-3): the recorded sales-channel decision history (who/what/when/why).</summary>
        public IReadOnlyList<BakeryChannelDecision> ChannelDecisions => channelDecisions;

        /// <summary>Lane 10a (H-3): the latest recorded channel decision, or null when no decision is on record.</summary>
        public BakeryChannelDecision LatestChannelDecision =>
            channelDecisions.Count == 0 ? null : channelDecisions[channelDecisions.Count - 1];

        /// <summary>D1D: fulfilled wholesale deliveries.</summary>
        public IReadOnlyList<BakeryWholesaleDeliveryRecord> WholesaleDeliveries => wholesaleDeliveries;

        /// <summary>D1D: delivery runs (staged, in transit, delivered, awaiting pickup).</summary>
        public IReadOnlyList<BakeryDeliveryRun> DeliveryRuns => deliveryRuns;

        /// <summary>D1D: proofing-rack slots gating the daily proof schedule.</summary>
        public int ProofingRackSlots => proofingRackSlots;

        /// <summary>
        /// D1D: the owner's scarcity policy (Canon §7.3E). Unspecified
        /// coerces to the Lane 9a owner decision ProtectRetailCounter
        /// (see the supplyProtection field comment); the coercion is a
        /// deserialization fallback for old saves, never a decision.
        /// </summary>
        public BakerySupplyProtectionPolicy SupplyProtection
        {
            get => supplyProtection;
            set => supplyProtection = value == BakerySupplyProtectionPolicy.Unspecified
                ? BakerySupplyProtectionPolicy.ProtectRetailCounter
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
                // Priority spoilage integration: advance an attached
                // condition state by the SAME authored rule, at the
                // same simulation-time step. ApplyElapsedTime
                // recomputes Fresh/DayOld/Stale from the whole-day age
                // exactly as AgeToDay does, so the attached state can
                // never disagree with the lot's condition; flour and
                // every other item are untouched (no authored rule).
                if (lot.HasPerishableCondition)
                {
                    PerishableLotState state = PerishableLotState.FromSaveDto(lot.PerishableCondition);
                    int elapsed = Math.Max(0, dayIndex - (int)Math.Floor(state.AsOfDay));
                    if (elapsed > 0)
                    {
                        lot.PerishableCondition = PerishableLotState.ApplyElapsedTime(state, elapsed, "ambient")
                            .CaptureSaveDto();
                    }
                }
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
        /// Priority 9 Slice F3: one log-only bakery-dispense record per
        /// dispense line. The source is the bin lot the units came from;
        /// the destination is the dough batch's custody, which is not a lot,
        /// so the destination key is Empty. The cause key names the batch,
        /// the input, the source lot, and the day, so a re-dispense after a
        /// failed prep (custody returned to the bin) is a new transfer,
        /// not a duplicate.
        /// </summary>
        private void RecordDispense(BakeryDoughBatch batch, List<BakeryFlourDispenseLine> lines, int dayIndex)
        {
            if (CustodyLog == null || lines == null) return;
            foreach (BakeryFlourDispenseLine line in lines)
            {
                if (line == null || line.UnitsTaken <= 0) continue;
                CustodyLog.Record(new CustodyTransferRecord(
                    dayIndex, line.FlourName, line.UnitsTaken,
                    new LotKey(CustodyTransferLog.BakeryAuthority(BusinessInstanceId), line.LotId.ToString()),
                    LotKey.Empty,
                    CustodyTransferLog.BakeryAuthority(BusinessInstanceId),
                    CustodyTransferLog.BatchCustodian(batch.BatchId),
                    "dispense-" + batch.BatchId + "-" + line.FlourName + "-" + line.LotId.ToString() + "-day" + dayIndex,
                    CustodyTransferLog.CauseBakeryDispense), out _);
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
                // Priority 9 Slice F3: custody recording only, once custody
                // is committed to the batch — one record per dispense line
                // (per source lot). A prep that fails later returns its
                // custody to the bin and records nothing. A logging failure
                // never fails the prep.
                if (CustodyLog != null)
                {
                    RecordDispense(batch, flourLines, dayIndex);
                    RecordDispense(batch, notionLines, dayIndex);
                }
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

            // Priority 9 Slice F4: the bread lot's own canonical
            // provenance key rides in its InputProvenance, appended once
            // (never a second time for the same lot) — the free-text
            // input lines above are kept verbatim.
            string canonicalKey = LotIdentity.CanonicalProvenanceKey(
                CustodyTransferLog.BakeryAuthority(BusinessInstanceId), lot.LotId.ToString());
            if (!lot.InputProvenance.Contains(canonicalKey))
                lot.InputProvenance.Add(canonicalKey);

            // Priority spoilage integration: the condition state is
            // CREATED with the lot at its creation site. Bread is the
            // only item with an authored aging rule, so it starts
            // Fresh/Fresh on its bake day, anchored at that day, with
            // the bake as its source. The flour-to-bread history link
            // lives in InputProvenance (free text) above; the state's
            // own handling history starts empty at the bake.
            lot.PerishableCondition = new PerishableLotState(
                new LotKey(CustodyTransferLog.BakeryAuthority(BusinessInstanceId), lot.LotId.ToString()),
                BakeryBreadCatalog.BreadLoafId,
                spec.YieldUnitsPerBatch,
                dayIndex)
                .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Fresh)
                .WithSource("bake:" + batch.BatchId, BusinessInstanceId, "bakery:" + BusinessInstanceId)
                .CaptureSaveDto();
            lot.HasPerishableCondition = true;

            breadShelf.Add(lot);
            // Priority 9 Slice F3: custody recording only. The batch's
            // custody becomes the bread lot. A logging failure never fails
            // the bake.
            if (CustodyLog != null)
            {
                CustodyLog.Record(new CustodyTransferRecord(
                    dayIndex, lot.ProductId, spec.YieldUnitsPerBatch,
                    new LotKey(CustodyTransferLog.BakeryAuthority(BusinessInstanceId), "batch:" + batch.BatchId),
                    new LotKey(CustodyTransferLog.BakeryAuthority(BusinessInstanceId), lot.LotId.ToString()),
                    CustodyTransferLog.BatchCustodian(batch.BatchId),
                    CustodyTransferLog.BakeryAuthority(BusinessInstanceId),
                    "bake:" + batch.BatchId,
                    CustodyTransferLog.CauseBakeryBake), out _);
            }
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
                // Priority spoilage integration: split BEFORE taking.
                // The taken part carries its own condition state onto
                // the sale record (the buyer carries it forward); the
                // remainder stays on the lot, unit-synced by TakeUnits.
                // TakeUnits alone cannot do this: it knows the lot, not
                // the record. A stateless lot sells exactly as before.
                PerishableLotState.SaveDto takenCondition = default;
                bool hasTakenCondition = false;
                if (lot.HasPerishableCondition && lot.UnitCount > 0)
                {
                    int takeable = Math.Min(remaining, lot.UnitCount);
                    if (takeable > 0)
                    {
                        (PerishableLotState remainingState, PerishableLotState takenState) =
                            PerishableLotState.FromSaveDto(lot.PerishableCondition)
                                .Split(takeable, dayIndex);
                        takenCondition = takenState.CaptureSaveDto();
                        hasTakenCondition = true;
                        lot.PerishableCondition = remainingState.CaptureSaveDto();
                    }
                }
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
                    PerishableCondition = takenCondition,
                    HasPerishableCondition = hasTakenCondition,
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

            // Priority spoilage integration: the sale records are the
            // runtime's own record of the sale — they are retained on
            // the runtime (and so reach the save DTO), not only returned
            // to the caller. SellBread previously returned them without
            // retaining them, so Sales stayed empty in live operation
            // even though the DTO round-trips them and
            // SaveLoad_RoundTrip_PreservesShelfBatchesAndWasteDedupe
            // pins the retention.
            sales.AddRange(records);
            return records;
        }

        /// <summary>
        /// Pass 2 (task system): SURVIVING BAKERY MODEL decision. The shop
        /// runtime (PlanBatch / WorkDay, used by the slice runs) is the
        /// production bakery model. The staged 9-step chain is NOT a second
        /// production model: it is a batch-execution path whose finished
        /// goods reach customers ONLY through this transfer. A staged lot
        /// is converted, once, into a sellable shop-runtime lot.
        /// </summary>
        private readonly List<string> transferredStagedLotIds = new List<string>();

        /// <summary>Staged lot ids already transferred into this shop shelf (idempotence guard).</summary>
        public IReadOnlyList<string> TransferredStagedLotIds => transferredStagedLotIds;

        /// <summary>
        /// Pass 2: closes F16 (staged-to-sellable transfer). Converts one
        /// staged bread lot into a sellable shop-runtime lot and records its
        /// staged id so the same staged lot can never transfer twice.
        /// Aging is preserved from the BAKED day, not reset to the transfer
        /// day: a loaf baked yesterday is day-old when transferred today,
        /// never fresh again. That choice is recorded here and in the Pass 2
        /// review doc; if Kennedy wants transfer-day aging instead, this
        /// method is the single place to change it. Refuses unknown products,
        /// non-positive units, and already-transferred staged ids, moving
        /// nothing in any refusal case. The caller (Pass2BakeryStagedTransfer)
        /// removes the staged lot from the staged shelf only after this
        /// method succeeds, so a failed transfer leaves staged stock intact.
        /// </summary>
        public BakeryBreadLot ReceiveTransferredBreadLot(
            BakeryStagedBreadLot staged, int dayIndex, EntityIdRegistry idRegistry, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (staged == null)
            {
                diag.Add("BakeryShopRuntime: staged transfer refused — no staged lot supplied.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(staged.LotId))
            {
                diag.Add("BakeryShopRuntime: staged transfer refused — staged lot has no id.");
                return null;
            }
            if (transferredStagedLotIds.Contains(staged.LotId))
            {
                diag.Add($"BakeryShopRuntime: staged transfer refused — staged lot '{staged.LotId}' already transferred; no second lot.");
                return null;
            }
            if (!BakeryBreadCatalog.IsKnownProduct(staged.ProductId))
            {
                diag.Add($"BakeryShopRuntime: staged transfer refused — unknown product '{staged.ProductId}'.");
                return null;
            }
            if (staged.Units <= 0)
            {
                diag.Add($"BakeryShopRuntime: staged transfer refused — staged lot '{staged.LotId}' has no units.");
                return null;
            }

            var lot = new BakeryBreadLot
            {
                LotId = idRegistry != null ? idRegistry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                ProductId = staged.ProductId,
                BakedDayIndex = staged.BakedDayIndex,
                UnitCount = staged.Units,
                Condition = BakeryGoodsCondition.Fresh,
            };
            if (staged.InputProvenance != null) lot.InputProvenance.AddRange(staged.InputProvenance);
            lot.InputProvenance.Add($"transferred from staged lot {staged.LotId} (batch {staged.BatchId})");
            // Age to the TRANSFER day from the preserved baked day: a loaf
            // baked yesterday is day-old when transferred today, never fresh.
            lot.AgeToDay(dayIndex);
            // Priority spoilage integration: the transferred lot carries
            // a state anchored at its BAKED day, aged to the transfer
            // day by the authored rule — never anchored at the transfer
            // day, which would rejuvenate it. The staged lot carries
            // no state of its own, so the state is built fresh here.
            lot.PerishableCondition = PerishableLotState.ApplyElapsedTime(
                new PerishableLotState(
                    new LotKey(CustodyTransferLog.BakeryAuthority(BusinessInstanceId), lot.LotId.ToString()),
                    BakeryBreadCatalog.BreadLoafId,
                    staged.Units,
                    staged.BakedDayIndex)
                    .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Fresh)
                    .WithSource("staged-transfer:" + staged.LotId, BusinessInstanceId, "bakery:" + BusinessInstanceId),
                Math.Max(0, dayIndex - staged.BakedDayIndex), "ambient")
                .CaptureSaveDto();
            lot.HasPerishableCondition = true;
            breadShelf.Add(lot);
            transferredStagedLotIds.Add(staged.LotId);
            diag.Add($"BakeryShopRuntime: staged lot '{staged.LotId}' transferred as shop lot {lot.LotId} "
                + $"({staged.Units} {staged.ProductId}, baked day {staged.BakedDayIndex}).");
            return lot;
        }

        #region D1D Wholesale accounts (Canon §7.3E)

        /// <summary>
        /// D1D: books a wholesale supply agreement with a repeat buyer
        /// (boarding house, hotel, restaurant, store, saloon, camp — Canon
        /// §7.3E). Returns a rejection string, or null on success.
        /// Lane 10a (H-3/H-6): the booking is recorded as a channel
        /// decision. Callers without a decision context (day/decider) get
        /// the honest "unspecified" record — the day is -1 (not supplied),
        /// never a fabricated day 0. The scenario path
        /// (ScenarioOpeningWholesaleAccounts) and the morning
        /// re-evaluation pass the real day and decider.
        /// </summary>
        public string AddWholesaleAccount(BakeryWholesaleAccount account, List<string> diag)
        {
            return AddWholesaleAccount(account, -1, BakeryChannelDecision.UnspecifiedDecider, diag);
        }

        /// <summary>
        /// D1D: books a wholesale supply agreement with a repeat buyer,
        /// recording the booking as a channel decision (H-3). An account
        /// comes into existence only by an explicit config entry, a
        /// voluntary recorded opt-in through this method, or a recorded
        /// decision — never implicitly (H-6: wholesale is opt-in only).
        /// Returns a rejection string, or null on success.
        /// </summary>
        public string AddWholesaleAccount(BakeryWholesaleAccount account, int dayIndex, string decider, List<string> diag)
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
            RecordChannelDecision(dayIndex, decider, BakeryChannelDecision.AccountAddedKind,
                "absent", account.AccountId,
                $"wholesale account added: {account.UnitsPerDelivery}x {account.ProductId} every {account.DeliveryCadenceDays} day(s) to {account.BuyerBusinessId} at {account.PricePerUnitCents}c/unit"
                + (dayIndex < 0 ? " (no decision context supplied by caller)" : string.Empty),
                diag, wholesalePriceCentsPerUnit: account.PricePerUnitCents);
            return null;
        }

        /// <summary>
        /// D1D: turns due wholesale accounts into planned dough batches
        /// (Canon §7.3E: large recurring buyers improve throughput visibility).
        /// Accounts due within the planning lead window share eligible
        /// finished stock and active production in deterministic due-date
        /// order; each lot or batch is allocated at most once. Only uncovered
        /// shortfall is planned. Finished stock due today is counted only
        /// when the configured supply policy protects it from the retail
        /// counter. Planning never bakes; <see cref="WorkDay"/> executes.
        /// </summary>
        public List<string> PlanWholesaleBatches(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var planned = new List<string>();
            var dueAccounts = new List<BakeryWholesaleAccount>();
            foreach (BakeryWholesaleAccount account in wholesaleAccounts)
            {
                if (account == null || !account.IsActive) continue;
                if (account.NextDeliveryDayIndex > dayIndex + BakeryBreadCatalog.WholesalePlanningLeadDays) continue;
                if (string.IsNullOrEmpty(BakeryBreadCatalog.GetSpec(account.ProductId).ProductId)) continue;
                dueAccounts.Add(account);
            }

            // A production batch and a finished lot are shared finite goods.
            // Allocate them once, in due-date/account order, instead of
            // letting every account independently claim the whole pipeline.
            dueAccounts.Sort((a, b) =>
            {
                int byDueDate = a.NextDeliveryDayIndex.CompareTo(b.NextDeliveryDayIndex);
                return byDueDate != 0
                    ? byDueDate
                    : string.Compare(a.AccountId, b.AccountId, StringComparison.Ordinal);
            });

            AgeShelf(dayIndex, diag);
            var availableLots = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (BakeryBreadLot lot in breadShelf)
            {
                if (lot == null || !lot.CanSell || string.IsNullOrWhiteSpace(lot.LotId.ToString())) continue;
                string lotId = lot.LotId.ToString();
                if (!availableLots.ContainsKey(lotId)) availableLots.Add(lotId, Math.Max(0, lot.UnitCount));
            }

            var activeBatchUnits = new Dictionary<string, int>(StringComparer.Ordinal);
            var activeBatches = new List<BakeryDoughBatch>();
            foreach (BakeryDoughBatch batch in batches)
            {
                if (batch == null || !batch.IsActive || string.IsNullOrWhiteSpace(batch.BatchId)) continue;
                if (activeBatchUnits.ContainsKey(batch.BatchId)) continue;
                int yield = BakeryBreadCatalog.GetSpec(batch.ProductId).YieldUnitsPerBatch;
                if (yield <= 0) continue;
                activeBatchUnits.Add(batch.BatchId, yield);
                activeBatches.Add(batch);
            }
            activeBatches.Sort((a, b) =>
            {
                int byScheduledDay = a.ScheduledDayIndex.CompareTo(b.ScheduledDayIndex);
                return byScheduledDay != 0
                    ? byScheduledDay
                    : string.Compare(a.BatchId, b.BatchId, StringComparison.Ordinal);
            });

            foreach (BakeryWholesaleAccount account in dueAccounts)
            {
                BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(account.ProductId);
                int deliveryDay = Math.Max(dayIndex, account.NextDeliveryDayIndex);
                int covered = 0;

                // Finished goods are distinct from work in progress and
                // count only when product and contract freshness permit.
                // Future deliveries use active production cover here; shelf
                // units are not assumed safely committed across intervening
                // retail sales without a durable lot reservation authority.
                // Likewise, a counter-priority policy means a shelf lot is
                // not promised to wholesale by this planning estimate.
                var lots = new List<BakeryBreadLot>();
                if (account.NextDeliveryDayIndex <= dayIndex
                    && supplyProtection == BakerySupplyProtectionPolicy.ProtectWholesaleContracts)
                {
                    foreach (BakeryBreadLot lot in breadShelf)
                    {
                        if (lot == null || !string.Equals(lot.ProductId, account.ProductId, StringComparison.Ordinal)) continue;
                        int ageAtDelivery = deliveryDay - lot.BakedDayIndex;
                        bool freshnessAllows = account.Freshness != BakeryWholesaleFreshness.FreshOnly || ageAtDelivery <= 0;
                        if (ageAtDelivery < 0 || ageAtDelivery > (account.Freshness == BakeryWholesaleFreshness.FreshOnly ? 0 : 1)) continue;
                        if (freshnessAllows) lots.Add(lot);
                    }
                }
                // Match fulfillment's freshest-first ordering.
                lots.Sort((a, b) => b.BakedDayIndex.CompareTo(a.BakedDayIndex));
                foreach (BakeryBreadLot lot in lots)
                {
                    string lotId = lot.LotId.ToString();
                    if (!availableLots.TryGetValue(lotId, out int available) || available <= 0) continue;
                    int take = Math.Min(Math.Max(0, account.UnitsPerDelivery) - covered, available);
                    if (take <= 0) break;
                    availableLots[lotId] = available - take;
                    covered += take;
                }

                // Allocate each active batch's expected output at most once.
                // A batch scheduled after a delivery cannot cover that earlier
                // obligation; late obligations may use today's re-preparation.
                foreach (BakeryDoughBatch batch in activeBatches)
                {
                    if (covered >= account.UnitsPerDelivery) break;
                    if (!string.Equals(batch.ProductId, account.ProductId, StringComparison.Ordinal)
                        || batch.ScheduledDayIndex > deliveryDay) continue;
                    if (!activeBatchUnits.TryGetValue(batch.BatchId, out int available) || available <= 0) continue;
                    int take = Math.Min(Math.Max(0, account.UnitsPerDelivery) - covered, available);
                    activeBatchUnits[batch.BatchId] = available - take;
                    covered += take;
                }

                int needed = Math.Max(0, account.UnitsPerDelivery) - covered;
                int toPlan = (needed + spec.YieldUnitsPerBatch - 1) / spec.YieldUnitsPerBatch;
                if (toPlan <= 0)
                {
                    diag.Add($"BakeryShopRuntime: wholesale account {account.AccountId} already covered ({covered} units in finished stock and scheduled production).");
                    continue;
                }

                var ids = PlanBatch(account.ProductId, toPlan, dayIndex, diag);
                if (ids == null) continue; // refusal already noted loudly by PlanBatch
                planned.AddRange(ids);
                int outputStillNeeded = needed;
                foreach (string id in ids)
                {
                    int output = spec.YieldUnitsPerBatch;
                    int allocatedToThisAccount = Math.Min(output, outputStillNeeded);
                    outputStillNeeded -= allocatedToThisAccount;
                    activeBatchUnits[id] = output - allocatedToThisAccount;
                    foreach (BakeryDoughBatch batch in batches)
                    {
                        if (batch != null && string.Equals(batch.BatchId, id, StringComparison.Ordinal))
                        {
                            activeBatches.Add(batch);
                            break;
                        }
                    }
                }
                activeBatches.Sort((a, b) =>
                {
                    int byScheduledDay = a.ScheduledDayIndex.CompareTo(b.ScheduledDayIndex);
                    return byScheduledDay != 0
                        ? byScheduledDay
                        : string.Compare(a.BatchId, b.BatchId, StringComparison.Ordinal);
                });
                diag.Add($"BakeryShopRuntime: planned {toPlan} batch(es) for wholesale account {account.AccountId} " +
                    $"(delivery day {account.NextDeliveryDayIndex}, {account.BuyerBusinessId}).");
            }

            return planned;
        }

        /// <summary>
        /// Plans a conservative one-day retail response from recorded bakery
        /// counter sales. Only observed sales create a production signal; the
        /// planner nets current retail-available finished stock and active
        /// production not needed for near-term wholesale obligations. It does
        /// not infer guaranteed town demand or create a standing stock target.
        /// </summary>
        public List<string> PlanRetailBatches(int dayIndex, List<string> diag)
        {
            return PlanRetailBatches(dayIndex, 0, diag);
        }

        public List<string> PlanRetailBatches(int dayIndex, int openHouseholdBreadNeedUnits, List<string> diag)
        {
            diag = diag ?? diagnostics;
            const int historyDays = 3;
            int firstDay = Math.Max(0, dayIndex - historyDays);
            int elapsedDays = Math.Max(1, dayIndex - firstDay);
            int observedUnits = 0;
            foreach (BakerySaleRecord sale in sales)
            {
                if (sale == null || !string.Equals(sale.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal)) continue;
                if (sale.DayIndex < firstDay || sale.DayIndex >= dayIndex) continue;
                observedUnits += Math.Max(0, sale.Units);
            }
            int targetUnits = observedUnits <= 0 ? 0 : (observedUnits + elapsedDays - 1) / elapsedDays;
            int openNeedSignal = Math.Min(Math.Max(0, openHouseholdBreadNeedUnits), BakeryBreadCatalog.BreadYieldPerBatch);
            targetUnits = Math.Max(targetUnits, openNeedSignal);
            if (targetUnits <= 0)
            {
                diag.Add($"BakeryShopRuntime: retail production planner found no recorded bread sales in the {historyDays}-day history and no open household bread need; no retail batch planned.");
                return new List<string>();
            }

            AgeShelf(dayIndex, diag);
            int shelfForRetail = RetailAllowanceFor(BakeryBreadCatalog.BreadLoafId, dayIndex);
            // Use the same finite-stock allocation order as wholesale
            // planning. Subtracting all near-term commitments from all active
            // output double-counts contracts already covered by finished
            // shelf lots and can schedule a needless additional retail batch.
            int retailPipeline = ActiveBreadUnitsUncommittedForRetail(dayIndex);
            int uncovered = Math.Max(0, targetUnits - shelfForRetail - retailPipeline);
            int batchesToPlan = (uncovered + BakeryBreadCatalog.BreadYieldPerBatch - 1)
                / BakeryBreadCatalog.BreadYieldPerBatch;
            if (batchesToPlan <= 0)
            {
                diag.Add($"BakeryShopRuntime: retail production forecast is covered ({targetUnits} daily target units from {observedUnits} recent sales and {openHouseholdBreadNeedUnits} open household need units, {shelfForRetail} retail-available finished, {retailPipeline} uncommitted active production).");
                return new List<string>();
            }

            List<string> ids = PlanBatch(BakeryBreadCatalog.BreadLoafId, batchesToPlan, dayIndex, diag);
            if (ids != null)
                diag.Add($"BakeryShopRuntime: planned {ids.Count} retail bread batch(es) from {observedUnits} recorded counter sale unit(s) across {elapsedDays} day(s) and {openHouseholdBreadNeedUnits} open household need unit(s); physical inputs and capacity remain enforced by production.");
            return ids ?? new List<string>();
        }

        /// <summary>
        /// Estimates active bread output left for retail after allocating
        /// eligible finished lots and active batches to near-term wholesale
        /// obligations in the same due-date/account order used by
        /// <see cref="PlanWholesaleBatches"/>. This is a planning estimate
        /// only; it creates no reservation and moves no stock.
        /// </summary>
        private int ActiveBreadUnitsUncommittedForRetail(int dayIndex)
        {
            var availableLots = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (BakeryBreadLot lot in breadShelf)
            {
                if (lot == null || !lot.CanSell
                    || !string.Equals(lot.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal)) continue;
                string lotId = lot.LotId.ToString();
                if (string.IsNullOrWhiteSpace(lotId) || availableLots.ContainsKey(lotId)) continue;
                availableLots.Add(lotId, Math.Max(0, lot.UnitCount));
            }

            var activeUnits = new Dictionary<string, int>(StringComparer.Ordinal);
            var activeBatches = new List<BakeryDoughBatch>();
            foreach (BakeryDoughBatch batch in batches)
            {
                if (batch == null || !batch.IsActive
                    || batch.ScheduledDayIndex > dayIndex + BakeryBreadCatalog.WholesalePlanningLeadDays
                    || !string.Equals(batch.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(batch.BatchId)
                    || activeUnits.ContainsKey(batch.BatchId)) continue;
                activeUnits.Add(batch.BatchId, BakeryBreadCatalog.BreadYieldPerBatch);
                activeBatches.Add(batch);
            }
            activeBatches.Sort((a, b) =>
            {
                int byDay = a.ScheduledDayIndex.CompareTo(b.ScheduledDayIndex);
                return byDay != 0 ? byDay : string.Compare(a.BatchId, b.BatchId, StringComparison.Ordinal);
            });

            var dueAccounts = new List<BakeryWholesaleAccount>();
            foreach (BakeryWholesaleAccount account in wholesaleAccounts)
            {
                if (account == null || !account.IsActive
                    || !string.Equals(account.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal)
                    || account.NextDeliveryDayIndex > dayIndex + BakeryBreadCatalog.WholesalePlanningLeadDays) continue;
                dueAccounts.Add(account);
            }
            dueAccounts.Sort((a, b) =>
            {
                int byDueDate = a.NextDeliveryDayIndex.CompareTo(b.NextDeliveryDayIndex);
                return byDueDate != 0
                    ? byDueDate
                    : string.Compare(a.AccountId, b.AccountId, StringComparison.Ordinal);
            });

            foreach (BakeryWholesaleAccount account in dueAccounts)
            {
                int deliveryDay = Math.Max(dayIndex, account.NextDeliveryDayIndex);
                int covered = 0;
                if (account.NextDeliveryDayIndex <= dayIndex
                    && supplyProtection == BakerySupplyProtectionPolicy.ProtectWholesaleContracts)
                {
                    var eligibleLots = new List<BakeryBreadLot>();
                    foreach (BakeryBreadLot lot in breadShelf)
                    {
                        if (lot == null || !string.Equals(lot.ProductId, account.ProductId, StringComparison.Ordinal)) continue;
                        int ageAtDelivery = deliveryDay - lot.BakedDayIndex;
                        bool freshnessAllows = account.Freshness != BakeryWholesaleFreshness.FreshOnly || ageAtDelivery <= 0;
                        if (ageAtDelivery < 0 || ageAtDelivery > (account.Freshness == BakeryWholesaleFreshness.FreshOnly ? 0 : 1)) continue;
                        if (freshnessAllows) eligibleLots.Add(lot);
                    }
                    eligibleLots.Sort((a, b) => b.BakedDayIndex.CompareTo(a.BakedDayIndex));
                    foreach (BakeryBreadLot lot in eligibleLots)
                    {
                        string lotId = lot.LotId.ToString();
                        if (!availableLots.TryGetValue(lotId, out int available) || available <= 0) continue;
                        int take = Math.Min(Math.Max(0, account.UnitsPerDelivery) - covered, available);
                        if (take <= 0) break;
                        availableLots[lotId] = available - take;
                        covered += take;
                    }
                }

                foreach (BakeryDoughBatch batch in activeBatches)
                {
                    if (covered >= account.UnitsPerDelivery) break;
                    if (batch.ScheduledDayIndex > deliveryDay
                        || !activeUnits.TryGetValue(batch.BatchId, out int available) || available <= 0) continue;
                    int take = Math.Min(Math.Max(0, account.UnitsPerDelivery) - covered, available);
                    activeUnits[batch.BatchId] = available - take;
                    covered += take;
                }
            }

            int uncommitted = 0;
            foreach (int units in activeUnits.Values) uncommitted += Math.Max(0, units);
            return uncommitted;
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
                // Priority spoilage integration: split BEFORE taking,
                // as in SellBread — the taken part's condition state
                // rides the delivery record; the remainder stays on
                // the lot, unit-synced by TakeUnits. A stateless lot
                // contributes a default (empty) entry, so the list
                // stays parallel to SellerLotIds / UnitsByLot.
                PerishableLotState.SaveDto takenCondition = default;
                bool hasTakenCondition = false;
                if (lot.HasPerishableCondition && lot.UnitCount > 0)
                {
                    int takeable = Math.Min(remaining, lot.UnitCount);
                    if (takeable > 0)
                    {
                        (PerishableLotState remainingState, PerishableLotState takenState) =
                            PerishableLotState.FromSaveDto(lot.PerishableCondition)
                                .Split(takeable, dayIndex);
                        takenCondition = takenState.CaptureSaveDto();
                        hasTakenCondition = true;
                        lot.PerishableCondition = remainingState.CaptureSaveDto();
                    }
                }
                int taken = lot.TakeUnits(remaining);
                if (taken <= 0) continue;
                remaining -= taken;

                record.SellerLotIds.Add(lot.LotId.ToString());
                record.UnitsByLot.Add(taken);
                record.ConditionByLot.Add(lot.Condition);
                record.BakedDayByLot.Add(lot.BakedDayIndex);
                record.PerishableConditionsByLot.Add(hasTakenCondition
                    ? takenCondition
                    : new PerishableLotState.SaveDto());
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
        /// Plan 01 (2026-11-06, slice 1B): records a completed wholesale
        /// transfer against an account WITHOUT touching the shelf — the
        /// single schedule-advance authority for the store path. The
        /// transfer driver (Review2BakeryToStoreWholesaleDriver) already
        /// moved the goods and settled the cash exactly once, so this
        /// method moves neither: it appends the delivery record (with the
        /// provenance lines the caller captured before the transfer, so
        /// the record keeps the full upstream chain) and advances the
        /// account to its next delivery day. It must NOT be called for a
        /// transfer FulfillWholesale already took (double-take): the
        /// store account's schedule advances here and only here.
        /// Returns a refusal string, or null on success. A short delivery
        /// is recorded against the same contract terms and advances the
        /// cadence once; only the transferred units are settled. This keeps
        /// real shortages visible without leaving a paid transfer due for a
        /// second charge on the next daily driver pass.
        /// </summary>
        public string RecordWholesaleTransfer(
            string accountId, string sellerLotId, int units, int pricePerUnitCents,
            int dayIndex, List<string> provenanceLines, List<string> diag)
        {
            return RecordWholesaleTransfer(
                accountId,
                new[] { sellerLotId ?? string.Empty },
                new[] { units },
                pricePerUnitCents,
                dayIndex,
                provenanceLines,
                diag);
        }

        /// <summary>
        /// Records one account delivery fulfilled from several distinct
        /// finished lots. Each source lot and quantity remains explicit in
        /// the durable delivery record; the account cadence advances once
        /// for the aggregate contract delivery.
        /// </summary>
        public string RecordWholesaleTransfer(
            string accountId, IReadOnlyList<string> sellerLotIds, IReadOnlyList<int> unitsByLot,
            int pricePerUnitCents, int dayIndex, List<string> provenanceLines, List<string> diag)
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
                diag.Add($"BakeryShopRuntime: wholesale transfer record refused — no active account '{accountId}'.");
                return $"BakeryShopRuntime: wholesale transfer record refused — no active account '{accountId}'.";
            }
            if (sellerLotIds == null || unitsByLot == null || sellerLotIds.Count == 0
                || sellerLotIds.Count != unitsByLot.Count)
            {
                diag.Add($"BakeryShopRuntime: wholesale transfer record refused — account '{accountId}' needs parallel source-lot ids and quantities; no advance, nothing recorded.");
                return $"BakeryShopRuntime: wholesale transfer record refused — account '{accountId}' needs parallel source-lot ids and quantities; no advance, nothing recorded.";
            }
            int units = 0;
            for (int i = 0; i < sellerLotIds.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(sellerLotIds[i]) || unitsByLot[i] <= 0)
                {
                    diag.Add($"BakeryShopRuntime: wholesale transfer record refused — account '{accountId}' has an empty source lot or non-positive lot quantity; no advance, nothing recorded.");
                    return $"BakeryShopRuntime: wholesale transfer record refused — account '{accountId}' has an empty source lot or non-positive lot quantity; no advance, nothing recorded.";
                }
                units = checked(units + unitsByLot[i]);
            }
            if (units > account.UnitsPerDelivery)
            {
                diag.Add($"BakeryShopRuntime: wholesale transfer record refused — account '{accountId}' allows 1–{account.UnitsPerDelivery} units per delivery, not {units}; no advance, nothing recorded.");
                return $"BakeryShopRuntime: wholesale transfer record refused — account '{accountId}' allows 1–{account.UnitsPerDelivery} units per delivery, not {units}; no advance, nothing recorded.";
            }
            if (dayIndex < account.NextDeliveryDayIndex)
            {
                diag.Add($"BakeryShopRuntime: wholesale transfer record refused — account '{accountId}' is not due until day {account.NextDeliveryDayIndex}; no advance, nothing recorded.");
                return $"BakeryShopRuntime: wholesale transfer record refused — account '{accountId}' is not due until day {account.NextDeliveryDayIndex}; no advance, nothing recorded.";
            }
            for (int i = 0; i < wholesaleDeliveries.Count; i++)
            {
                BakeryWholesaleDeliveryRecord prior = wholesaleDeliveries[i];
                if (prior != null
                    && string.Equals(prior.AccountId, accountId, StringComparison.Ordinal)
                    && prior.DayIndex == dayIndex)
                {
                    diag.Add($"BakeryShopRuntime: wholesale transfer record refused — account '{accountId}' already has a delivery record for day {dayIndex}; no duplicate record.");
                    return $"BakeryShopRuntime: wholesale transfer record refused — account '{accountId}' already has a delivery record for day {dayIndex}; no duplicate record.";
                }
            }

            var record = new BakeryWholesaleDeliveryRecord
            {
                DeliveryId = $"{businessInstanceId}-wholesale-delivery-{deliverySeq++}",
                AccountId = account.AccountId,
                BuyerBusinessId = account.BuyerBusinessId,
                BuyerType = account.BuyerType,
                ProductId = account.ProductId,
                Units = units,
                PricePerUnitCents = Math.Max(0, pricePerUnitCents),
                DayIndex = dayIndex,
                FullyCovered = units == account.UnitsPerDelivery,
            };
            for (int i = 0; i < sellerLotIds.Count; i++)
            {
                record.SellerLotIds.Add(sellerLotIds[i].Trim());
                record.UnitsByLot.Add(unitsByLot[i]);
            }
            if (provenanceLines != null) record.ProvenanceLines.AddRange(provenanceLines);
            wholesaleDeliveries.Add(record);
            account.NextDeliveryDayIndex += Math.Max(1, account.DeliveryCadenceDays);

            diag.Add($"BakeryShopRuntime: wholesale transfer recorded {record.DeliveryId} — {units}/{account.UnitsPerDelivery} {account.ProductId} " +
                $"to {account.BuyerBusinessId} ({sellerLotIds.Count} source lot(s), day {dayIndex})" +
                (record.FullyCovered ? string.Empty : " — SHORT delivery recorded against finite available stock") +
                $"; next delivery day {account.NextDeliveryDayIndex}.");
            return null;
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
        /// Lane 9a (H-4): units of a product committed to wholesale
        /// accounts due today, for the staged-batch gate. A voluntary
        /// wholesale account no longer vetoes staged retail production;
        /// only the units actually due today are netted out of the retail
        /// stock figure, so a due delivery is still protected from being
        /// shorted by a staged batch.
        /// </summary>
        public int WholesaleUnitsDueToday(string productId, int dayIndex) =>
            WholesaleUnitsDue(productId, dayIndex);

        /// <summary>
        /// Lane 9a (H-4): the retail stock figure the staged-batch gate
        /// compares against open household need — the counter allowance
        /// net of due-today wholesale units. Under ProtectRetailCounter
        /// the allowance is the full shelf, so due units are netted here.
        /// Under the other policies the allowance already nets due
        /// commitments (ProtectWholesaleContracts) or shares them
        /// (ProportionalShare); netting again would double-subtract.
        /// </summary>
        public int RetailUnitsNetOfDueWholesale(string productId, int dayIndex, List<string> diag = null)
        {
            int allowance = RetailUnitsAvailableForSale(productId, dayIndex, diag);
            if (supplyProtection != BakerySupplyProtectionPolicy.ProtectRetailCounter) return allowance;
            return Math.Max(0, allowance - WholesaleUnitsDue(productId, dayIndex));
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

            // ProtectWholesaleContracts: due accounts first.
            return Math.Max(0, totalSellable - Math.Max(0, committed));
        }

        /// <summary>
        /// Returns bakery-owned units the retail counter can actually sell
        /// after aging current lots and protecting due wholesale commitments.
        /// This is a read for production planning; it never reserves or moves
        /// stock. The same allowance is enforced again by SellBread at sale.
        /// </summary>
        public int RetailUnitsAvailableForSale(string productId, int dayIndex, List<string> diag = null)
        {
            if (!BakeryBreadCatalog.IsKnownProduct(productId)) return 0;
            AgeShelf(dayIndex, diag);
            return RetailAllowanceFor(productId, dayIndex);
        }

        #endregion

        #region Lane 10a Sales-channel choice (Canon §7.3E/§7.5, H-2/H-3/H-5/H-6)

        /// <summary>
        /// Lane 10a (H-2): resolves Glenn's initial sales-channel policy
        /// ONCE, from the scenario-authored config value, and records the
        /// resolution as a decision. Authored value wins; absent
        /// (Unspecified) means ProtectRetailCounter — the §19 retail-first
        /// owner decision (Lane 9a), recorded as "scenario default,
        /// retail-first per §19". Idempotent: a runtime that already holds
        /// an initial-policy decision is not re-resolved. The code field
        /// initializer and the save-DTO default remain only as
        /// deserialization fallbacks for old saves, never as a decision.
        /// Returns a rejection string, or null on success (resolution is
        /// never refused — the worst case is the recorded default).
        /// </summary>
        public string ResolveInitialChannelPolicy(BakerySupplyProtectionPolicy authoredPolicy, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (BakeryChannelDecision existing in channelDecisions)
            {
                if (existing != null && string.Equals(existing.Kind, BakeryChannelDecision.InitialPolicyKind, StringComparison.Ordinal))
                {
                    diag.Add($"BakeryShopRuntime: initial channel policy already resolved on day {existing.DayIndex} ({existing.NewValue}); not re-resolved.");
                    return null;
                }
            }

            BakerySupplyProtectionPolicy previous = supplyProtection;
            BakerySupplyProtectionPolicy resolved = authoredPolicy == BakerySupplyProtectionPolicy.Unspecified
                ? BakerySupplyProtectionPolicy.ProtectRetailCounter
                : authoredPolicy;
            supplyProtection = resolved;
            string reason = authoredPolicy == BakerySupplyProtectionPolicy.Unspecified
                ? "scenario default, retail-first per §19 (Lane 9a owner decision; no initial policy authored in config)"
                : "scenario-authored initial policy";
            RecordChannelDecision(dayIndex, BakeryChannelDecision.ScenarioDecider,
                BakeryChannelDecision.InitialPolicyKind, previous.ToString(), resolved.ToString(), reason, diag);
            return null;
        }

        /// <summary>
        /// Lane 10a (H-2/H-3): records the standing policy of a runtime
        /// with no decision on record — a legacy save whose history predates
        /// the decision record, or an opening that never resolved one. The
        /// policy itself is UNCHANGED; only the fact that it stands, and
        /// when it was first observed, is recorded. A runtime that already
        /// holds any decision is left alone. Called by the morning
        /// re-evaluation before it evaluates, so the history is never
        /// silently empty.
        /// </summary>
        public string RecordStandingPolicyAsDecision(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (channelDecisions.Count > 0)
            {
                diag.Add("BakeryShopRuntime: a channel decision is already on record; the standing policy needs no backstop entry.");
                return null;
            }

            RecordChannelDecision(dayIndex, BakeryChannelDecision.ScenarioDecider,
                BakeryChannelDecision.StandingPolicyKind, supplyProtection.ToString(), supplyProtection.ToString(),
                "standing policy recorded on first observation — no prior decision on record (legacy save or unconfigured opening); the policy itself is unchanged",
                diag);
            return null;
        }

        /// <summary>
        /// Lane 10a (H-3): sets the owner's scarcity policy and records the
        /// change as a channel decision (who/what/when/why + observed
        /// inputs). A set to the value already in force records nothing
        /// and is not a decision. Unspecified is refused: it is not a
        /// settable policy (the plain SupplyProtection property setter
        /// still coerces it for deserialization/tests, without recording).
        /// Returns a rejection string, or null on success.
        /// </summary>
        public string SetSupplyProtection(
            BakerySupplyProtectionPolicy policy, int dayIndex, string decider, string reason, List<string> diag,
            int recordedRetailAverageUnitsPerDay = -1, int openHouseholdBreadNeedUnits = -1,
            int dueWholesaleUnits = -1, int retailPriceCentsPerUnit = -1, int wholesalePriceCentsPerUnit = -1)
        {
            diag = diag ?? diagnostics;
            if (policy == BakerySupplyProtectionPolicy.Unspecified)
                return "BakeryShopRuntime: Unspecified is not a settable channel policy — the current policy stands.";
            BakerySupplyProtectionPolicy previous = supplyProtection;
            if (previous == policy)
            {
                diag.Add($"BakeryShopRuntime: channel policy already {policy}; no decision recorded.");
                return null;
            }

            supplyProtection = policy;
            RecordChannelDecision(dayIndex, decider, BakeryChannelDecision.PolicyChangeKind,
                previous.ToString(), policy.ToString(), reason, diag,
                recordedRetailAverageUnitsPerDay, openHouseholdBreadNeedUnits, dueWholesaleUnits,
                retailPriceCentsPerUnit, wholesalePriceCentsPerUnit);
            return null;
        }

        /// <summary>
        /// Lane 10a (H-3/H-5): pauses or resumes one wholesale account by
        /// recorded decision (IsActive = false/true). Ending an account is
        /// a lawful transition, never a silent wipe: booked accounts stay
        /// booked in existing saves until a decision ends them, and the
        /// entry names the decider, day and reason. A set to the value
        /// already in force records nothing. Returns a rejection string,
        /// or null on success.
        /// </summary>
        public string SetWholesaleAccountActive(string accountId, bool isActive, int dayIndex, string decider, string reason, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(accountId))
                return "BakeryShopRuntime: a wholesale account id is required to change its active state.";
            BakeryWholesaleAccount account = null;
            foreach (BakeryWholesaleAccount candidate in wholesaleAccounts)
            {
                if (candidate != null && string.Equals(candidate.AccountId, accountId, StringComparison.Ordinal))
                {
                    account = candidate;
                    break;
                }
            }
            if (account == null)
                return $"BakeryShopRuntime: no wholesale account '{accountId}' — nothing paused or resumed.";

            if (account.IsActive == isActive)
            {
                diag.Add($"BakeryShopRuntime: wholesale account '{accountId}' is already {(isActive ? "active" : "paused")}; no decision recorded.");
                return null;
            }

            bool previous = account.IsActive;
            account.IsActive = isActive;
            RecordChannelDecision(dayIndex, decider,
                isActive ? BakeryChannelDecision.AccountResumedKind : BakeryChannelDecision.AccountPausedKind,
                previous ? "active" : "paused", isActive ? "active" : "paused", reason, diag,
                wholesalePriceCentsPerUnit: account.PricePerUnitCents);
            return null;
        }

        /// <summary>
        /// Lane 10a (H-5): the 3-day recorded counter-sale average for
        /// bread loaves — the same window PlanRetailBatches plans from
        /// (historyDays = 3, sales strictly before today, ceiling
        /// average). Only observed sales create a demand signal.
        /// </summary>
        public int RecordedRetailAverageUnitsPerDay(int dayIndex)
        {
            const int historyDays = 3;
            int firstDay = Math.Max(0, dayIndex - historyDays);
            int elapsedDays = Math.Max(1, dayIndex - firstDay);
            int observedUnits = 0;
            foreach (BakerySaleRecord sale in sales)
            {
                if (sale == null || !string.Equals(sale.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal)) continue;
                if (sale.DayIndex < firstDay || sale.DayIndex >= dayIndex) continue;
                observedUnits += Math.Max(0, sale.Units);
            }
            return observedUnits <= 0 ? 0 : (observedUnits + elapsedDays - 1) / elapsedDays;
        }

        /// <summary>
        /// Lane 10a (H-5): nameplate daily bread production capacity in
        /// loaves — the smaller of the oven firing budget (installed ovens
        /// × firings per oven per day) and the baker labour budget (baker
        /// minutes per day ÷ hands-on minutes per batch), times the yield
        /// per batch. Readiness (kit, oven components, fuel, flour) still
        /// gates actual production — this is the planning ceiling the
        /// channel rule compares demand against, not a promise that the
        /// loaves bake. Zero when no oven is installed.
        /// </summary>
        public int DailyProductionCapacityUnits()
        {
            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(BakeryBreadCatalog.BreadLoafId);
            int minutesPerBatch = Math.Max(1, spec.PrepMinutes + spec.BakeMinutes);
            int labourBatches = BakerMinutesPerDay / minutesPerBatch;
            int ovenBatches = Math.Max(0, ovens.Count) * BakeryBreadCatalog.FiringsPerOvenPerDay;
            return Math.Max(0, Math.Min(labourBatches, ovenBatches)) * BakeryBreadCatalog.BreadYieldPerBatch;
        }

        /// <summary>
        /// Lane 10a (H-5): Glenn's deterministic morning sales-channel
        /// re-evaluation, run before planning each morning by
        /// BakeryWholesaleDailyDriver.RunDay. The policy is re-evaluated
        /// against the morning signals (3-day recorded counter sales, open
        /// household need, retail-available shelf, due wholesale terms and
        /// revenue, counter price, nameplate capacity); a change is a
        /// recorded decision, never silent, and no change when the signals
        /// are ambiguous. Delivery cost is caller-supplied (0 when no haul
        /// terms are registered at planning time — the revenue comparison
        /// then uses gross wholesale revenue). A bakery with no NPC owner
        /// bound (player-owned bakery, D-04 owner decision) is SKIPPED,
        /// not decided for: the standing policy is kept and the gap is
        /// named in the diagnostic. Returns a rejection string, or null
        /// (evaluation ran, applied, or was skipped with cause).
        /// </summary>
        public string ReevaluateChannelChoice(int dayIndex, int openHouseholdBreadNeedUnits, int deliveryCostCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (OwnerPersonId < 0)
            {
                diag.Add($"BakeryShopRuntime: morning channel re-evaluation skipped on day {dayIndex} — no NPC owner is bound (player-owned bakery, D-04 owner decision): the standing policy {supplyProtection} is kept and no decision is recorded.");
                return null;
            }

            // Backstop (H-2/H-3): a bound runtime with no decision on
            // record (legacy save, or an opening that never resolved one)
            // records its standing policy first, unchanged.
            if (channelDecisions.Count == 0)
                RecordStandingPolicyAsDecision(dayIndex, diag);

            int recordedAvg = RecordedRetailAverageUnitsPerDay(dayIndex);
            int retailAvailable = RetailUnitsAvailableForSale(BakeryBreadCatalog.BreadLoafId, dayIndex, diag);
            int dueUnits = WholesaleUnitsDueToday(BakeryBreadCatalog.BreadLoafId, dayIndex);
            int dueRevenue = DueWholesaleRevenueCents(dayIndex);
            int capacity = DailyProductionCapacityUnits();
            int retailPrice = prices.BreadLoafFreshCents;
            int wholesalePrice = dueUnits > 0 ? dueRevenue / dueUnits : -1;
            string decider = "P" + OwnerPersonId;
            var signals = new BakeryChannelSignals
            {
                DayIndex = dayIndex,
                RecordedRetailAverageUnitsPerDay = recordedAvg,
                OpenHouseholdBreadNeedUnits = Math.Max(0, openHouseholdBreadNeedUnits),
                RetailUnitsAvailable = retailAvailable,
                DueWholesaleUnits = dueUnits,
                DueWholesaleRevenueCents = dueRevenue,
                DeliveryCostCents = Math.Max(0, deliveryCostCents),
                DailyProductionCapacityUnits = capacity,
                RetailPriceCentsPerUnit = retailPrice,
                CurrentPolicy = supplyProtection,
            };

            BakeryChannelProposal proposal = BakeryChannelChoiceEvaluator.Evaluate(signals);
            if (proposal == null)
            {
                diag.Add($"BakeryShopRuntime: morning channel re-evaluation day {dayIndex}: no proposal — current policy {supplyProtection} stands.");
                return null;
            }

            string reasons = JoinReasons(proposal.Reasons);
            if (proposal.RecommendedPolicy != BakerySupplyProtectionPolicy.Unspecified
                && proposal.RecommendedPolicy != supplyProtection)
            {
                SetSupplyProtection(proposal.RecommendedPolicy, dayIndex, decider, reasons, diag,
                    recordedAvg, Math.Max(0, openHouseholdBreadNeedUnits), dueUnits, retailPrice, wholesalePrice);
            }

            if (proposal.PauseDueWholesaleAccounts)
            {
                var due = new List<BakeryWholesaleAccount>();
                foreach (BakeryWholesaleAccount account in wholesaleAccounts)
                {
                    if (account == null || !account.IsActive) continue;
                    if (!string.Equals(account.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal)) continue;
                    if (account.NextDeliveryDayIndex > dayIndex) continue;
                    due.Add(account);
                }
                foreach (BakeryWholesaleAccount account in due)
                    SetWholesaleAccountActive(account.AccountId, false, dayIndex, decider, reasons, diag);
            }

            if (proposal.RecommendedPolicy == BakerySupplyProtectionPolicy.Unspecified
                && !proposal.PauseDueWholesaleAccounts)
            {
                diag.Add($"BakeryShopRuntime: morning channel re-evaluation day {dayIndex}: no change — {reasons}; policy {supplyProtection} stands.");
            }
            return null;
        }

        private int DueWholesaleRevenueCents(int dayIndex)
        {
            int revenue = 0;
            foreach (BakeryWholesaleAccount account in wholesaleAccounts)
            {
                if (account == null || !account.IsActive) continue;
                if (!string.Equals(account.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal)) continue;
                if (account.NextDeliveryDayIndex > dayIndex) continue;
                revenue += Math.Max(0, account.UnitsPerDelivery) * Math.Max(0, account.PricePerUnitCents);
            }
            return revenue;
        }

        private static string JoinReasons(List<string> reasons)
        {
            if (reasons == null || reasons.Count == 0) return "no reason recorded";
            return string.Join("; ", reasons);
        }

        private void RecordChannelDecision(
            int dayIndex, string decider, string kind, string oldValue, string newValue, string reason, List<string> diag,
            int recordedRetailAverageUnitsPerDay = -1, int openHouseholdBreadNeedUnits = -1,
            int dueWholesaleUnits = -1, int retailPriceCentsPerUnit = -1, int wholesalePriceCentsPerUnit = -1)
        {
            diag = diag ?? diagnostics;
            var decision = new BakeryChannelDecision
            {
                DayIndex = dayIndex,
                Decider = string.IsNullOrWhiteSpace(decider) ? BakeryChannelDecision.UnspecifiedDecider : decider,
                Kind = kind ?? string.Empty,
                OldValue = oldValue ?? string.Empty,
                NewValue = newValue ?? string.Empty,
                Reason = reason ?? string.Empty,
                RecordedRetailAverageUnitsPerDay = recordedRetailAverageUnitsPerDay,
                OpenHouseholdBreadNeedUnits = openHouseholdBreadNeedUnits,
                DueWholesaleUnits = dueWholesaleUnits,
                RetailPriceCentsPerUnit = retailPriceCentsPerUnit,
                WholesalePriceCentsPerUnit = wholesalePriceCentsPerUnit,
            };
            channelDecisions.Add(decision);
            diag.Add($"BakeryShopRuntime: channel decision — {decision.Summary()}");
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

            /// <summary>Pass 2: staged lot ids already transferred (additive; old saves load empty).</summary>
            public List<string> TransferredStagedLotIds = new List<string>();
            public List<BakerySaleRecord> Sales = new List<BakerySaleRecord>();
            public List<BakeryWasteRecord> Waste = new List<BakeryWasteRecord>();
            public List<string> WasteNotedLotIds = new List<string>();

            /// <summary>D1D: wholesale supply agreements.</summary>
            public List<BakeryWholesaleAccount> WholesaleAccounts = new List<BakeryWholesaleAccount>();

            /// <summary>D1D: fulfilled wholesale deliveries.</summary>
            public List<BakeryWholesaleDeliveryRecord> WholesaleDeliveries = new List<BakeryWholesaleDeliveryRecord>();

            /// <summary>
            /// Lane 10a (H-3): the persisted sales-channel decision history
            /// (additive; old saves load empty — see LoadFromSaveDto, same
            /// pattern as TransferredStagedLotIds).
            /// </summary>
            public List<BakeryChannelDecision> ChannelDecisions = new List<BakeryChannelDecision>();

            /// <summary>D1D: delivery runs.</summary>
            public List<BakeryDeliveryRun> DeliveryRuns = new List<BakeryDeliveryRun>();

            /// <summary>D1D: proofing-rack slots.</summary>
            public int ProofingRackSlots = BakeryBreadCatalog.DefaultProofingRackSlots;

            /// <summary>
            /// D1D: the owner's scarcity policy. Deserialization fallback
            /// only (Lane 9a owner decision ProtectRetailCounter); a
            /// persisted policy is restored by LoadFromSaveDto, and the
            /// choice itself is recorded by the Lane 10a decision
            /// machinery, never by this default.
            /// </summary>
            public BakerySupplyProtectionPolicy SupplyProtection = BakerySupplyProtectionPolicy.ProtectRetailCounter;

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
            dto.TransferredStagedLotIds.AddRange(transferredStagedLotIds);
            dto.Sales.AddRange(sales);
            dto.Waste.AddRange(waste);
            dto.WasteNotedLotIds.AddRange(wasteNotedLotIds);
            dto.WholesaleAccounts.AddRange(wholesaleAccounts);
            dto.WholesaleDeliveries.AddRange(wholesaleDeliveries);
            dto.ChannelDecisions.AddRange(channelDecisions);
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

            transferredStagedLotIds.Clear();
            if (dto.TransferredStagedLotIds != null)
            {
                transferredStagedLotIds.AddRange(dto.TransferredStagedLotIds);
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

            // Lane 10a (H-3): additive — old saves carry no decision
            // history and load empty, exactly like TransferredStagedLotIds.
            channelDecisions.Clear();
            if (dto.ChannelDecisions != null)
            {
                foreach (var decision in dto.ChannelDecisions)
                {
                    if (decision != null) channelDecisions.Add(decision);
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
