using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Blacksmith
{
    /// <summary>
    /// D4K: the repair queue economy — shared authority for the blacksmith and
    /// wheelwright shops (Tech X §6.4: ONE shared work-order authority lives in
    /// the Blacksmith namespace; the wheelwright already queues through
    /// RepairQueue). Canon §7.2C/D: work orders move intake → diagnosis →
    /// quote → agreed → work → complete. This file deepens the middle and the
    /// tail of that lifecycle WITHOUT replacing anything:
    ///
    /// - RepairQuoteBook: a quote is a RECORDED OFFER (parts lines from real
    ///   lots + labor minutes x shop rate), explicitly accepted or declined —
    ///   no silent billing. Canon §7.2D pricing freedom is preserved: the
    ///   quote is the shop's ASK; the customer can still negotiate the agreed
    ///   price (RepairQueue.AgreeTerms takes the final number).
    /// - RepairQueueScheduler: queue position + estimated completion from
    ///   shop throughput — smith-hours are the constrained resource
    ///   (TurnaroundEstimator.DefaultShopLaborMinutesPerDay, the existing
    ///   work-time budget), not bays. Overbooking refuses loudly.
    /// - RepairPrioritization: rush (one-step urgency bump + labor premium),
    ///   breakdown-vs-maintenance classification. Regulars get NO default
    ///   bump — canon-gated fork, parameterized and default-off.
    /// - RepairPartsProvenance: every part on a quote traces to a real lot —
    ///   nails as iron-stock consumption (EQP convention), lumber from the
    ///   yard's lots, leather from tannery lots, ironwork from real smith-made
    ///   parts. Shortfalls refuse loudly; no synthetic stock.
    /// - RepairPickupLedger: repaired items await pickup or delivery; the
    ///   asset's LocationId moves with it (physical truth). Unclaimed after
    ///   a parameterized window (research-gated: the 1888 shape was 3 months).
    /// - ArtisanLienRegister: the artisan's lien as a RECORDED CLAIM securing
    ///   the unpaid bill — common-law possessory right (retain until paid).
    ///   Never auto-seizure, never auto-sale: enforcement needs an explicit
    ///   notice and an explicit operator sale; proceeds pay the claim + costs
    ///   first, surplus returns to the owner (the 1888 statute shape).
    ///
    /// Every rate, window, and threshold below is CALIBRATION (Canon Part XV)
    /// — tuning, not canon.
    /// </summary>

    // ================= quotes: the recorded offer =================

    /// <summary>D4K: lifecycle of one recorded repair quote offer.</summary>
    public enum RepairQuoteStatus
    {
        Unspecified = 0,
        Offered = 1,   // on the table, awaiting the customer's answer
        Accepted = 2,  // customer said yes — drives RepairQueue.AgreeTerms
        Declined = 3,  // customer said no — order leaves the queue as Declined
        Expired = 4,   // quote lapsed unanswered — order stays Diagnosed, re-quotable
    }

    /// <summary>
    /// D4K: one parts line on a quote — units of a REAL lot at a real price.
    /// LotId is the provenance: iron-stock lot, lumber lot, tannery leather
    /// lot, or a smith-made part asset id. Lines never name synthetic stock.
    /// </summary>
    [Serializable]
    public sealed class RepairQuotePartLine
    {
        public string MaterialId = string.Empty;   // "iron-stock", "lumber", "leather", "smith-ironwork"
        public string MaterialLabel = string.Empty; // display, e.g. "nails (iron stock)"
        public string LotId = string.Empty;        // the real lot / part asset this line draws from
        public string ProvenanceNote = string.Empty;
        public int Units;
        public int UnitPriceCents;

        public int LineTotalCents => Math.Max(0, Units) * Math.Max(0, UnitPriceCents);

        public RepairQuotePartLine() { }

        public RepairQuotePartLine(string materialId, string materialLabel, string lotId,
            int units, int unitPriceCents, string provenanceNote)
        {
            MaterialId = materialId ?? string.Empty;
            MaterialLabel = materialLabel ?? string.Empty;
            LotId = lotId ?? string.Empty;
            Units = Math.Max(0, units);
            UnitPriceCents = Math.Max(0, unitPriceCents);
            ProvenanceNote = provenanceNote ?? string.Empty;
        }
    }

    /// <summary>
    /// D4K: the shop's pricing policy knobs — parts at cost + markup, labor
    /// at the shop rate, billed by the started hour (same shape as the
    /// wheelwright's W6b pricing; the smith's ask uses the same defaults so
    /// the two trades quote comparably).
    /// </summary>
    public static class RepairQuotePricing
    {
        /// <summary>Calibration: artisan shop labor rate. Tuning, not canon.</summary>
        public const int DefaultShopRateCentsPerHour = 25;

        /// <summary>Calibration: parts markup over material cost. Tuning, not canon.</summary>
        public const float DefaultPartsMarkup01 = 0.20f;

        /// <summary>Calibration: how long a quote stands. Tuning, not canon.</summary>
        public const int DefaultQuoteValidityDays = 14;

        /// <summary>
        /// Calibration: rush labor premium. Matches the wheelwright's build-order
        /// rush premium (WheelwrightBuildOrderBook.RushLaborPremium = 1.25) so
        /// rush means the same thing in both shops. Tuning, not canon.
        /// </summary>
        public const float DefaultRushLaborPremium01 = 1.25f;

        public static void PricePartsPlusLabor(
            List<RepairQuotePartLine> partLines, int laborMinutes,
            int shopRateCentsPerHour, float partsMarkup01,
            out int partsSubtotalCents, out int laborSubtotalCents, out int totalCents)
        {
            partsSubtotalCents = 0;
            if (partLines != null)
            {
                int rawParts = 0;
                foreach (RepairQuotePartLine line in partLines)
                    if (line != null) rawParts += line.LineTotalCents;
                partsSubtotalCents = (int)Math.Round(rawParts * (1f + Math.Max(0f, partsMarkup01)));
            }
            int hours = (int)Math.Ceiling(Math.Max(0, laborMinutes) / 60.0);
            laborSubtotalCents = hours * Math.Max(0, shopRateCentsPerHour);
            totalCents = partsSubtotalCents + laborSubtotalCents;
        }

        /// <summary>Applies the rush premium to the labor leg of a quote.</summary>
        public static int ApplyRushPremium(int laborSubtotalCents, float rushPremium01 = DefaultRushLaborPremium01)
        {
            return (int)Math.Round(Math.Max(0, laborSubtotalCents) * Math.Max(1f, rushPremium01));
        }
    }

    /// <summary>
    /// D4K: one recorded quote offer. The ask breaks into parts lines (each
    /// traced to a real lot) + labor (minutes x shop rate). The customer
    /// accepts or declines EXPLICITLY — an offer never silently becomes a
    /// bill. Canon §7.2D pricing freedom: the quote is the shop's ask; the
    /// AGREED price is still negotiated per order.
    /// </summary>
    [Serializable]
    public sealed class RepairQuoteOffer
    {
        public string OfferId = string.Empty; // RQ-0001
        public string WorkOrderId = string.Empty;
        public string ShopBusinessId = string.Empty;
        public string ShopBusinessName = string.Empty;
        public List<RepairQuotePartLine> PartLines = new List<RepairQuotePartLine>();
        public int LaborMinutes;
        public int ShopRateCentsPerHour = RepairQuotePricing.DefaultShopRateCentsPerHour;
        public float PartsMarkup01 = RepairQuotePricing.DefaultPartsMarkup01;
        public bool RushQuoted;
        public int PartsSubtotalCents;
        public int LaborSubtotalCents;
        public int TotalAskCents;
        public int QuotedDayIndex;
        public int ExpiresDayIndex;
        public RepairQuoteStatus Status = RepairQuoteStatus.Unspecified;

        // ---------- save DTO (inside the owning offer class) ----------

        [Serializable]
        public sealed class RepairQuoteOfferDto
        {
            public RepairQuoteOffer Offer = new RepairQuoteOffer();
        }

        public RepairQuoteOfferDto ToSaveDto()
        {
            return new RepairQuoteOfferDto { Offer = this };
        }

        public static RepairQuoteOffer FromSaveDto(RepairQuoteOfferDto dto)
        {
            if (dto == null || dto.Offer == null) return null;
            return dto.Offer;
        }
    }

    /// <summary>
    /// D4K: the shop's quote book. Issues recorded offers from diagnosed
    /// orders, moves them through accept/decline/expire. Acceptance drives
    /// the EXISTING RepairQueue.AgreeTerms path — the quote book records the
    /// ask, the queue still records the agreement.
    /// </summary>
    public sealed class RepairQuoteBook
    {
        private readonly List<RepairQuoteOffer> offers = new List<RepairQuoteOffer>();
        private int nextNumber = 1;

        public IReadOnlyList<RepairQuoteOffer> Offers => offers;

        /// <summary>
        /// Records the shop's ask on a diagnosed order. Refuses orders that
        /// are not Diagnosed — no quote before diagnosis (Canon §7.2C).
        /// </summary>
        public RepairQuoteOffer IssueOffer(
            RepairWorkOrder order,
            string shopBusinessId, string shopBusinessName,
            List<RepairQuotePartLine> partLines,
            int shopRateCentsPerHour, float partsMarkup01,
            int dayIndex, List<string> diagnostics,
            int validityDays = RepairQuotePricing.DefaultQuoteValidityDays)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (order == null)
            {
                diagnostics.Add("RepairQuoteBook: no work order supplied — quote refused.");
                return null;
            }
            if (order.Status != RepairOrderStatus.Diagnosed)
            {
                diagnostics.Add(
                    $"RepairQuoteBook: {order.WorkOrderId} is {order.Status}, not Diagnosed — " +
                    "no quote before diagnosis (Canon §7.2C).");
                return null;
            }
            var offer = new RepairQuoteOffer
            {
                OfferId = $"RQ-{nextNumber++:D4}",
                WorkOrderId = order.WorkOrderId,
                ShopBusinessId = shopBusinessId ?? string.Empty,
                ShopBusinessName = shopBusinessName ?? string.Empty,
                LaborMinutes = Math.Max(0, order.EstimatedLaborMinutes),
                ShopRateCentsPerHour = Math.Max(0, shopRateCentsPerHour),
                PartsMarkup01 = Math.Max(0f, partsMarkup01),
                RushQuoted = order.RushRequested,
                QuotedDayIndex = dayIndex,
                ExpiresDayIndex = dayIndex + Math.Max(1, validityDays),
                Status = RepairQuoteStatus.Offered,
            };
            if (partLines != null)
                foreach (RepairQuotePartLine line in partLines)
                    if (line != null) offer.PartLines.Add(line);
            RepairQuotePricing.PricePartsPlusLabor(
                offer.PartLines, offer.LaborMinutes,
                offer.ShopRateCentsPerHour, offer.PartsMarkup01,
                out offer.PartsSubtotalCents, out offer.LaborSubtotalCents, out offer.TotalAskCents);
            if (offer.RushQuoted)
            {
                offer.LaborSubtotalCents = RepairQuotePricing.ApplyRushPremium(offer.LaborSubtotalCents);
                offer.TotalAskCents = offer.PartsSubtotalCents + offer.LaborSubtotalCents;
            }
            offers.Add(offer);
            diagnostics.Add(
                $"RepairQuoteBook: {offer.OfferId} offered on {order.WorkOrderId} — " +
                $"{offer.PartsSubtotalCents}c parts + {offer.LaborSubtotalCents}c labor = {offer.TotalAskCents}c " +
                $"(expires day {offer.ExpiresDayIndex}).");
            return offer;
        }

        public RepairQuoteOffer Find(string offerId)
        {
            foreach (RepairQuoteOffer o in offers)
                if (string.Equals(o.OfferId, offerId, StringComparison.Ordinal))
                    return o;
            return null;
        }

        private static RepairWorkOrder FindOrder(RepairQueue queue, string workOrderId)
        {
            if (queue == null) return null;
            foreach (RepairWorkOrder o in queue.Orders)
                if (string.Equals(o.WorkOrderId, workOrderId, StringComparison.Ordinal))
                    return o;
            return null;
        }

        /// <summary>
        /// The customer accepts the offer — drives the existing
        /// RepairQueue.AgreeTerms at the offer's ask (or a negotiated figure
        /// the caller passes). Refuses expired/answered offers.
        /// </summary>
        public string AcceptOffer(
            string offerId, RepairQueue queue, int dayIndex, List<string> diagnostics,
            int? negotiatedPriceCents = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            RepairQuoteOffer offer = Find(offerId);
            if (offer == null) return $"RepairQuoteBook: unknown offer '{offerId}'.";
            if (offer.Status != RepairQuoteStatus.Offered)
                return $"RepairQuoteBook: {offerId} is {offer.Status}, not Offered.";
            if (dayIndex > offer.ExpiresDayIndex)
                return $"RepairQuoteBook: {offerId} expired on day {offer.ExpiresDayIndex} — re-quote.";
            RepairWorkOrder order = FindOrder(queue, offer.WorkOrderId);
            if (order == null)
                return $"RepairQuoteBook: work order '{offer.WorkOrderId}' not in this queue.";
            int price = negotiatedPriceCents ?? offer.TotalAskCents;
            string rejection = queue.AgreeTerms(order.WorkOrderId, price);
            if (rejection != null) return "RepairQuoteBook: " + rejection;
            offer.Status = RepairQuoteStatus.Accepted;
            diagnostics.Add(
                $"RepairQuoteBook: {offerId} accepted at {price}c — {order.WorkOrderId} now Quoted, terms agreed.");
            return null;
        }

        /// <summary>
        /// The customer declines — the order leaves the active queue as
        /// Declined (it is NOT silently dropped, and it is NOT billed).
        /// </summary>
        public string DeclineOffer(
            string offerId, RepairQueue queue, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            RepairQuoteOffer offer = Find(offerId);
            if (offer == null) return $"RepairQuoteBook: unknown offer '{offerId}'.";
            if (offer.Status != RepairQuoteStatus.Offered)
                return $"RepairQuoteBook: {offerId} is {offer.Status}, not Offered.";
            RepairWorkOrder order = FindOrder(queue, offer.WorkOrderId);
            if (order == null)
                return $"RepairQuoteBook: work order '{offer.WorkOrderId}' not in this queue.";
            order.Status = RepairOrderStatus.Declined;
            offer.Status = RepairQuoteStatus.Declined;
            queue.NotifyPriorityChanged(diagnostics);
            diagnostics.Add(
                $"RepairQuoteBook: {offerId} declined — {order.WorkOrderId} marked Declined, no bill raised.");
            return null;
        }

        /// <summary>
        /// Lapses unanswered offers past their expiry. The order stays
        /// Diagnosed — it can be re-quoted, never auto-billed.
        /// </summary>
        public List<string> ExpireOffers(int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var expired = new List<string>();
            foreach (RepairQuoteOffer offer in offers)
            {
                if (offer.Status != RepairQuoteStatus.Offered) continue;
                if (dayIndex <= offer.ExpiresDayIndex) continue;
                offer.Status = RepairQuoteStatus.Expired;
                expired.Add(offer.OfferId);
                diagnostics.Add(
                    $"RepairQuoteBook: {offer.OfferId} expired unanswered — order {offer.WorkOrderId} remains Diagnosed.");
            }
            return expired;
        }

        // ---------- save DTO (inside the owning book class) ----------

        [Serializable]
        public sealed class RepairQuoteBookDto
        {
            public List<RepairQuoteOffer> Offers = new List<RepairQuoteOffer>();
            public int NextNumber;
        }

        public RepairQuoteBookDto ToSaveDto()
        {
            var dto = new RepairQuoteBookDto { NextNumber = nextNumber };
            dto.Offers.AddRange(offers);
            return dto;
        }

        public void LoadFromSaveDto(RepairQuoteBookDto dto)
        {
            offers.Clear();
            nextNumber = 1;
            if (dto == null) return;
            foreach (RepairQuoteOffer o in dto.Offers)
                if (o != null) offers.Add(o);
            nextNumber = Math.Max(1, dto.NextNumber);
        }
    }

    // ================= throughput: smith-hours as the constraint =================

    /// <summary>D4K: one shop-day's repair labor allocation.</summary>
    [Serializable]
    public struct RepairShopDayEntry
    {
        public string WorkOrderId;
        public int Minutes;

        public RepairShopDayEntry(string workOrderId, int minutes)
        {
            WorkOrderId = workOrderId ?? string.Empty;
            Minutes = Math.Max(0, minutes);
        }
    }

    /// <summary>
    /// D4K: the shop's labor budget — the constrained resource is
    /// smith/wright-hours per day (the existing work-time budget:
    /// TurnaroundEstimator.DefaultShopLaborMinutesPerDay), not bays.
    /// A day plan books each open order's labor against real shop days in
    /// queue order; intake that cannot fit inside the booking horizon is
    /// refused LOUDLY instead of silently stacking.
    /// </summary>
    public sealed class RepairShopDayPlan
    {
        public int StartDayIndex;
        public int LaborMinutesPerDay;
        public int Craftsmen;
        public readonly Dictionary<int, List<RepairShopDayEntry>> Days =
            new Dictionary<int, List<RepairShopDayEntry>>();
        public readonly Dictionary<string, int> StartDayByOrder =
            new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> EndDayByOrder =
            new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>
    /// D4K: throughput scheduler over the shared repair queue. Orders are
    /// booked in queue order (emergency &gt; urgent &gt; routine, FIFO within
    /// urgency — RepairQueue), each day's labor capped at
    /// laborMinutesPerDay x craftsmen.
    /// </summary>
    public static class RepairQueueScheduler
    {
        /// <summary>Calibration: how far ahead the shop books. Tuning, not canon.</summary>
        public const int DefaultBookingHorizonDays = 60;

        /// <summary>Calibration: backlog past this is refused at intake. Tuning, not canon.</summary>
        public const int DefaultMaxQueueDays = 30;

        public static bool IsActive(RepairWorkOrder order)
        {
            return order != null
                && order.Status != RepairOrderStatus.Complete
                && order.Status != RepairOrderStatus.Paid
                && order.Status != RepairOrderStatus.Declined;
        }

        /// <summary>
        /// Books every active order's estimated labor against shop days,
        /// starting at currentDayIndex. Orders with no labor estimate take a
        /// full day's nominal slot so they still occupy the bench.
        /// </summary>
        public static RepairShopDayPlan BuildDayPlan(
            RepairQueue queue, int currentDayIndex,
            int laborMinutesPerDay = LandLedgers.Economy.Wheelwright.TurnaroundEstimator.DefaultShopLaborMinutesPerDay,
            int craftsmen = 1,
            List<string> diagnostics = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            var plan = new RepairShopDayPlan
            {
                StartDayIndex = currentDayIndex,
                LaborMinutesPerDay = Math.Max(1, laborMinutesPerDay),
                Craftsmen = Math.Max(1, craftsmen),
            };
            int daily = plan.LaborMinutesPerDay * plan.Craftsmen;
            var active = new List<RepairWorkOrder>();
            if (queue != null)
                foreach (RepairWorkOrder o in queue.Orders)
                    if (IsActive(o)) active.Add(o);
            active.Sort((a, b) => a.QueuePosition.CompareTo(b.QueuePosition));

            int day = currentDayIndex;
            int remainingToday = daily;
            foreach (RepairWorkOrder order in active)
            {
                int minutes = Math.Max(1, order.EstimatedLaborMinutes);
                bool started = false;
                while (minutes > 0)
                {
                    if (remainingToday <= 0) { day++; remainingToday = daily; }
                    int take = Math.Min(minutes, remainingToday);
                    if (!plan.Days.TryGetValue(day, out List<RepairShopDayEntry> entries))
                    {
                        entries = new List<RepairShopDayEntry>();
                        plan.Days[day] = entries;
                    }
                    entries.Add(new RepairShopDayEntry(order.WorkOrderId, take));
                    if (!started)
                    {
                        plan.StartDayByOrder[order.WorkOrderId] = day;
                        started = true;
                    }
                    plan.EndDayByOrder[order.WorkOrderId] = day;
                    minutes -= take;
                    remainingToday -= take;
                }
            }
            diagnostics.Add(
                $"RepairQueueScheduler: {active.Count} active orders booked over " +
                $"{plan.Days.Count} shop days from day {currentDayIndex} ({daily} labor-min/day).");
            return plan;
        }

        /// <summary>
        /// Loud overbooking check: when the current backlog already runs past
        /// the booking window, new intake is REFUSED with a named reason —
        /// the shop cannot do more than its smith-hours allow.
        /// Returns null when the shop can take the work.
        /// </summary>
        public static string CheckOverbooked(
            RepairQueue queue, int currentDayIndex,
            int laborMinutesPerDay = LandLedgers.Economy.Wheelwright.TurnaroundEstimator.DefaultShopLaborMinutesPerDay,
            int craftsmen = 1,
            int maxQueueDays = DefaultMaxQueueDays,
            List<string> diagnostics = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            RepairShopDayPlan plan = BuildDayPlan(queue, currentDayIndex, laborMinutesPerDay, craftsmen, diagnostics);
            int latest = currentDayIndex;
            foreach (int end in plan.EndDayByOrder.Values)
                if (end > latest) latest = end;
            int backlogDays = latest - currentDayIndex;
            if (backlogDays > Math.Max(1, maxQueueDays))
            {
                int activeCount = plan.StartDayByOrder.Count;
                return $"RepairQueueScheduler: REFUSED — backlog already runs {backlogDays} days " +
                       $"({activeCount} active orders, {plan.LaborMinutesPerDay * plan.Craftsmen} labor-min/day) — " +
                       $"over the {maxQueueDays}-day booking window. The shop cannot take more work until the queue clears.";
            }
            return null;
        }
    }

    // ================= prioritization =================

    /// <summary>
    /// D4K: the shop's prioritization policy knobs. Canon only fixes the
    /// urgency ladder (emergency interrupts — Canon §7.2) and the patience
    /// escalation (wheelwright policy). Everything else here is a
    /// PARAMETERIZED operator choice, defaults documented below.
    /// </summary>
    [Serializable]
    public sealed class RepairPriorityPolicy
    {
        /// <summary>
        /// Calibration: rush labor premium, shared with build orders.
        /// Tuning, not canon (canon says nothing about rush pricing on repairs).
        /// </summary>
        public float RushLaborPremium01 = RepairQuotePricing.DefaultRushLaborPremium01;

        /// <summary>
        /// Parameterized: what urgency a BREAKDOWN (asset unusable) gets at
        /// intake. Default Urgent — the asset cannot work, so it cannot wait
        /// behind routine jobs. Canon-gated: canon does not fix this.
        /// </summary>
        public RepairUrgency BreakdownIntakeUrgency = RepairUrgency.Urgent;

        /// <summary>
        /// FORK (canon/research-gated, default OFF): whether regular customers
        /// get a FIFO head-start within their urgency band. Canon §7.2E gives
        /// regulars book credit, not queue priority — so the default is 0
        /// (no bump). A shop owner may set it, and it is then explicit.
        /// </summary>
        public int RegularCustomerHeadStartDays = 0;
    }

    /// <summary>
    /// D4K: prioritization rules the operator applies on top of the queue's
    /// own urgency ladder. Rush = one urgency step up (emergency is the
    /// ceiling) + rush labor premium on the quote. Breakdown vs maintenance
    /// is classified from the asset's real condition — a wreck that cannot
    /// work is a breakdown; a worn-but-usable asset is maintenance.
    /// </summary>
    public static class RepairPrioritization
    {
        /// <summary>
        /// Requests rush handling on an order: one urgency step up, flagged
        /// so the quote prices the rush premium. Refuses orders already in
        /// work or finished — rush is an intake/diagnosis/quote lever.
        /// </summary>
        public static string RequestRush(
            RepairQueue queue, string workOrderId, List<string> diagnostics,
            RepairPriorityPolicy policy = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (queue == null) return "RepairPrioritization: no queue supplied.";
            RepairWorkOrder order = null;
            foreach (RepairWorkOrder o in queue.Orders)
                if (string.Equals(o.WorkOrderId, workOrderId, StringComparison.Ordinal))
                { order = o; break; }
            if (order == null) return $"RepairPrioritization: unknown work order '{workOrderId}'.";
            if (order.Status != RepairOrderStatus.Intake
                && order.Status != RepairOrderStatus.Diagnosed
                && order.Status != RepairOrderStatus.Quoted)
                return $"RepairPrioritization: {workOrderId} is {order.Status} — rush is an intake/diagnosis/quote lever.";
            if (order.RushRequested)
            {
                diagnostics.Add($"RepairPrioritization: {workOrderId} is already rush — no double premium.");
                return null;
            }
            if (order.Urgency < RepairUrgency.Emergency)
                order.Urgency = (RepairUrgency)((int)order.Urgency + 1);
            order.RushRequested = true;
            queue.NotifyPriorityChanged(diagnostics);
            float premium = policy != null ? policy.RushLaborPremium01 : RepairQuotePricing.DefaultRushLaborPremium01;
            diagnostics.Add(
                $"RepairPrioritization: {workOrderId} marked RUSH — urgency now {order.Urgency}, " +
                $"labor premium {premium:P0} will apply at quote.");
            return null;
        }

        /// <summary>
        /// Classifies the job from the asset's real condition: "breakdown"
        /// when the asset cannot work (EquipmentAsset.IsUsable), "maintenance"
        /// when it is worn but usable, "unknown" when there is no asset.
        /// </summary>
        public static string ClassifyJob(EquipmentAsset asset)
        {
            if (asset == null) return "unknown";
            return asset.IsUsable ? "maintenance" : "breakdown";
        }

        /// <summary>
        /// Intake helper: a breakdown defaults to the policy's breakdown
        /// urgency unless the caller already set one explicitly.
        /// </summary>
        public static RepairUrgency SuggestIntakeUrgency(
            string jobClass, RepairUrgency callerUrgency, RepairPriorityPolicy policy = null)
        {
            if (!string.Equals(jobClass, "breakdown", StringComparison.Ordinal))
                return callerUrgency;
            if (callerUrgency != RepairUrgency.Routine)
                return callerUrgency;
            return policy != null ? policy.BreakdownIntakeUrgency : RepairUrgency.Urgent;
        }
    }

    // ================= parts provenance =================

    /// <summary>
    /// A real leather lot, viewed from the tannery — D4K reads the lot (never
    /// the tannery's internals) so quotes can name the lot without coupling
    /// to TanneryRuntime.
    /// </summary>
    [Serializable]
    public struct LeatherLotView
    {
        public string LotId;
        public int Lbs;
        public string HideSourceNote;
        public string TanneryName;

        public LeatherLotView(string lotId, int lbs, string hideSourceNote, string tanneryName)
        {
            LotId = lotId ?? string.Empty;
            Lbs = Math.Max(0, lbs);
            HideSourceNote = hideSourceNote ?? string.Empty;
            TanneryName = tanneryName ?? string.Empty;
        }
    }

    /// <summary>
    /// D4K: parts provenance for repair quotes. Every line names a REAL lot
    /// or part — the smith's iron-stock lots (nails/hardware consume
    /// iron-stock per the EQP convention), the wheelwright's lumber lots
    /// (Tech X §6.1 lot discipline), smith-made ironwork parts with their
    /// makers, tannery leather lots. Shortfalls refuse loudly: a quote never
    /// invents stock.
    /// </summary>
    public static class RepairPartsProvenance
    {
        public const string IronStockMaterialId = "iron-stock";
        public const string LumberMaterialId = "lumber";
        public const string LeatherMaterialId = "leather";
        public const string SmithIronworkMaterialId = "smith-ironwork";

        /// <summary>
        /// EQP convention: nails and simple hardware are NOT a separate
        /// material — they are iron-stock consumption. A repair needing nails
        /// quotes (and consumes) iron-stock, labeled honestly.
        /// </summary>
        public static string NailsConventionNote =>
            "EQP convention: nails/simple hardware consume iron-stock — no separate nail material.";

        /// <summary>
        /// Builds iron-stock part lines for the smith, FIFO across the shop's
        /// real lots. Returns the refusal reason when stock is short — never
        /// a line for stock that does not exist.
        /// </summary>
        public static List<RepairQuotePartLine> SmithIronStockLines(
            BlacksmithRuntime smithy, int unitsNeeded, int unitPriceCents,
            List<string> diagnostics, out string refusal, string label = "iron stock")
        {
            diagnostics = diagnostics ?? new List<string>();
            refusal = null;
            var lines = new List<RepairQuotePartLine>();
            if (smithy == null) { refusal = "RepairPartsProvenance: no smithy supplied."; return lines; }
            if (unitsNeeded <= 0) return lines;
            if (smithy.MaterialOnHand(IronStockMaterialId) < unitsNeeded)
            {
                refusal = $"RepairPartsProvenance: short " +
                          $"{unitsNeeded - smithy.MaterialOnHand(IronStockMaterialId)}x iron-stock " +
                          $"for this repair — no synthetic stock (upstream-provenance).";
                return lines;
            }
            int remaining = unitsNeeded;
            foreach (string lotId in smithy.SmithLotIdsOnHand(IronStockMaterialId))
            {
                if (remaining <= 0) break;
                int lotUnits = smithy.LotUnitsOnHand(IronStockMaterialId, lotId);
                if (lotUnits <= 0) continue;
                int take = Math.Min(lotUnits, remaining);
                lines.Add(new RepairQuotePartLine(
                    IronStockMaterialId, label ?? "iron stock", lotId, take,
                    Math.Max(0, unitPriceCents),
                    $"smith iron-stock lot {lotId}"));
                remaining -= take;
            }
            if (remaining > 0)
            {
                // Lot ledger and unit stock disagreed — units exist but are not
                // lot-traced; refuse rather than quote untraced parts.
                refusal = "RepairPartsProvenance: iron-stock units exist but are not lot-traced — " +
                          "refusing to quote untraced parts (upstream-provenance).";
                return new List<RepairQuotePartLine>();
            }
            return lines;
        }

        /// <summary>
        /// Nails as iron-stock consumption (EQP convention): the label says
        /// nails, the material id stays iron-stock.
        /// </summary>
        public static List<RepairQuotePartLine> SmithNailsLines(
            BlacksmithRuntime smithy, int nailUnitsNeeded, int unitPriceCents,
            List<string> diagnostics, out string refusal)
        {
            return SmithIronStockLines(smithy, nailUnitsNeeded, unitPriceCents,
                diagnostics, out refusal, "nails (iron stock)");
        }

        /// <summary>
        /// Builds lumber part lines for the wheelwright, FIFO across the
        /// shop's real lumber lots (Tech X §6.1 lot discipline).
        /// </summary>
        public static List<RepairQuotePartLine> WheelwrightLumberLines(
            LandLedgers.Economy.Wheelwright.WheelwrightRuntime wright,
            int unitsNeeded, int unitPriceCents,
            List<string> diagnostics, out string refusal)
        {
            diagnostics = diagnostics ?? new List<string>();
            refusal = null;
            var lines = new List<RepairQuotePartLine>();
            if (wright == null) { refusal = "RepairPartsProvenance: no wheelwright shop supplied."; return lines; }
            if (unitsNeeded <= 0) return lines;
            if (wright.TotalLumberOnHand() < unitsNeeded)
            {
                refusal = $"RepairPartsProvenance: short " +
                          $"{unitsNeeded - wright.TotalLumberOnHand()}x lumber for this repair — " +
                          "no synthetic stock (upstream-provenance).";
                return lines;
            }
            int remaining = unitsNeeded;
            foreach (string lotId in wright.LumberLotIds())
            {
                if (remaining <= 0) break;
                int lotUnits = wright.LumberOnHand(lotId);
                if (lotUnits <= 0) continue;
                int take = Math.Min(lotUnits, remaining);
                lines.Add(new RepairQuotePartLine(
                    LumberMaterialId, "lumber", lotId, take,
                    Math.Max(0, unitPriceCents),
                    $"wheelwright lumber lot {lotId}"));
                remaining -= take;
            }
            return lines;
        }

        /// <summary>
        /// Builds ironwork part lines from the wheelwright's shelf of
        /// smith-made parts — each line names the real part asset and its
        /// maker (upstream provenance: smith → wheelwright).
        /// </summary>
        public static List<RepairQuotePartLine> WheelwrightIronworkLines(
            IReadOnlyList<EquipmentAsset> ironworkStock, int partsNeeded,
            int unitPriceCents, List<string> diagnostics, out string refusal)
        {
            diagnostics = diagnostics ?? new List<string>();
            refusal = null;
            var lines = new List<RepairQuotePartLine>();
            int onHand = 0;
            if (ironworkStock != null)
                foreach (EquipmentAsset p in ironworkStock)
                    if (p != null) onHand++;
            if (partsNeeded <= 0) return lines;
            if (onHand < partsNeeded)
            {
                refusal = $"RepairPartsProvenance: short {partsNeeded - onHand}x smith-made ironwork parts — " +
                          "the smith is upstream (FVS doctrine); order more, do not invent.";
                return lines;
            }
            int taken = 0;
            foreach (EquipmentAsset part in ironworkStock)
            {
                if (taken >= partsNeeded) break;
                if (part == null) continue;
                lines.Add(new RepairQuotePartLine(
                    SmithIronworkMaterialId, part.DisplayName, part.AssetId, 1,
                    Math.Max(0, unitPriceCents),
                    $"smith-made part {part.AssetId} by {part.MadeByBusinessName} ({part.MadeByBusinessId})"));
                taken++;
            }
            return lines;
        }

        /// <summary>
        /// Builds a leather part line against a real tannery lot. The tannery
        /// itself releases the leather — the quote only names the lot.
        /// </summary>
        public static List<RepairQuotePartLine> LeatherLines(
            LeatherLotView lot, int lbsNeeded, int priceCentsPerLb,
            List<string> diagnostics, out string refusal)
        {
            diagnostics = diagnostics ?? new List<string>();
            refusal = null;
            var lines = new List<RepairQuotePartLine>();
            if (lbsNeeded <= 0) return lines;
            if (string.IsNullOrWhiteSpace(lot.LotId))
            {
                refusal = "RepairPartsProvenance: no tannery leather lot named — leather is not conjured.";
                return lines;
            }
            if (lot.Lbs < lbsNeeded)
            {
                refusal = $"RepairPartsProvenance: tannery lot {lot.LotId} holds {lot.Lbs} lbs, " +
                          $"needs {lbsNeeded} — shortfall refused (upstream-provenance).";
                return lines;
            }
            lines.Add(new RepairQuotePartLine(
                LeatherMaterialId, "leather", lot.LotId, lbsNeeded,
                Math.Max(0, priceCentsPerLb),
                $"tannery lot {lot.LotId} ({lot.TanneryName}); hides: {lot.HideSourceNote}"));
            return lines;
        }
    }

    // ================= pickup / delivery =================

    /// <summary>D4K: where a finished repair sits.</summary>
    public enum RepairPickupStatus
    {
        Unspecified = 0,
        Ready = 1,      // work done, awaiting the owner
        PickedUp = 2,  // owner collected it at the shop
        Delivered = 3, // shop returned it to the owner's location
        Unclaimed = 4, // past the unclaimed window — see ArtisanLienRegister
    }

    /// <summary>
    /// D4K: one repaired item awaiting its owner. Pickup and delivery move
    /// the ASSET (its LocationId), never just a flag — physical truth.
    /// </summary>
    [Serializable]
    public sealed class RepairPickupRecord
    {
        public string RecordId = string.Empty; // RP-0001
        public string WorkOrderId = string.Empty;
        public string AssetId = string.Empty;
        public string AssetDescription = string.Empty;
        public string CustomerName = string.Empty;
        public string CustomerBusinessId = string.Empty;
        public int ReadyDayIndex;
        public int PickedUpDayIndex = -1;
        public int DeliveredDayIndex = -1;
        public string DeliveredLocationId = string.Empty;
        public RepairPickupStatus Status = RepairPickupStatus.Unspecified;
    }

    /// <summary>
    /// D4K: the shop's pickup ledger. Completed repairs are announced ready;
    /// the owner picks up or the shop delivers (the asset's LocationId moves
    /// with it). Items unclaimed past the parameterized window are flagged
    /// Unclaimed — the window default is research-gated (the 1888 artisan-lien
    /// shape: 3 months after the bill falls due).
    /// </summary>
    public sealed class RepairPickupLedger
    {
        /// <summary>
        /// Research-gated default: 90 days. The 1888 BC Mechanics' Lien Act let
        /// an artisan sell an unredeemed chattel after 3 months unpaid (with
        /// 2 weeks' newspaper notice). Calibration, not canon.
        /// </summary>
        public const int DefaultUnclaimedDays = 90;

        private readonly List<RepairPickupRecord> records = new List<RepairPickupRecord>();
        private int nextNumber = 1;

        public IReadOnlyList<RepairPickupRecord> Records => records;

        public RepairPickupRecord FindByOrder(string workOrderId)
        {
            foreach (RepairPickupRecord r in records)
                if (string.Equals(r.WorkOrderId, workOrderId, StringComparison.Ordinal))
                    return r;
            return null;
        }

        /// <summary>
        /// The shop announces a finished repair ready. Refuses orders that are
        /// not Complete — only finished work can be collected.
        /// </summary>
        public RepairPickupRecord NotifyReady(
            RepairWorkOrder order, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (order == null)
            {
                diagnostics.Add("RepairPickupLedger: no work order — nothing is ready.");
                return null;
            }
            if (order.Status != RepairOrderStatus.Complete)
            {
                diagnostics.Add(
                    $"RepairPickupLedger: {order.WorkOrderId} is {order.Status}, not Complete — " +
                    "only finished work can be collected.");
                return null;
            }
            RepairPickupRecord existing = FindByOrder(order.WorkOrderId);
            if (existing != null)
            {
                diagnostics.Add(
                    $"RepairPickupLedger: {order.WorkOrderId} already announced ready as {existing.RecordId}.");
                return existing;
            }
            var record = new RepairPickupRecord
            {
                RecordId = $"RP-{nextNumber++:D4}",
                WorkOrderId = order.WorkOrderId,
                AssetId = order.AssetId,
                AssetDescription = order.AssetDescription,
                CustomerName = order.CustomerName,
                CustomerBusinessId = order.CustomerBusinessId,
                ReadyDayIndex = dayIndex,
                Status = RepairPickupStatus.Ready,
            };
            records.Add(record);
            diagnostics.Add(
                $"RepairPickupLedger: {record.RecordId} — {order.AssetDescription} ready for {order.CustomerName} " +
                $"(order {order.WorkOrderId}).");
            return record;
        }

        /// <summary>The owner collects the repaired item at the shop.</summary>
        public string RecordPickup(string workOrderId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            RepairPickupRecord record = FindByOrder(workOrderId);
            if (record == null) return $"RepairPickupLedger: no pickup record for '{workOrderId}'.";
            if (record.Status != RepairPickupStatus.Ready)
                return $"RepairPickupLedger: {record.RecordId} is {record.Status}, not Ready.";
            record.Status = RepairPickupStatus.PickedUp;
            record.PickedUpDayIndex = dayIndex;
            diagnostics.Add($"RepairPickupLedger: {record.RecordId} picked up by {record.CustomerName} on day {dayIndex}.");
            return null;
        }

        /// <summary>
        /// The shop returns the repaired item to the owner's location. The
        /// asset's LocationId moves with it — physical truth (Canon §7.2C).
        /// </summary>
        public string RecordDelivery(
            string workOrderId, string locationId, EquipmentAsset asset,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            RepairPickupRecord record = FindByOrder(workOrderId);
            if (record == null) return $"RepairPickupLedger: no pickup record for '{workOrderId}'.";
            if (record.Status != RepairPickupStatus.Ready && record.Status != RepairPickupStatus.PickedUp)
                return $"RepairPickupLedger: {record.RecordId} is {record.Status} — nothing to deliver.";
            if (string.IsNullOrWhiteSpace(locationId))
                return $"RepairPickupLedger: {record.RecordId} — delivery needs a real destination.";
            if (asset != null && string.Equals(asset.AssetId, record.AssetId, StringComparison.Ordinal))
                asset.LocationId = locationId;
            record.Status = RepairPickupStatus.Delivered;
            record.DeliveredDayIndex = dayIndex;
            record.DeliveredLocationId = locationId;
            diagnostics.Add(
                $"RepairPickupLedger: {record.RecordId} delivered to {locationId} on day {dayIndex}.");
            return null;
        }

        /// <summary>
        /// Flags Ready records past the unclaimed window as Unclaimed. This
        /// only FLAGS — it never sells, seizes, or disposes; the lien register
        /// holds any claim (ArtisanLienRegister).
        /// </summary>
        public List<string> ScanUnclaimed(
            int dayIndex, List<string> diagnostics, int unclaimedDays = DefaultUnclaimedDays)
        {
            diagnostics = diagnostics ?? new List<string>();
            var flagged = new List<string>();
            foreach (RepairPickupRecord record in records)
            {
                if (record.Status != RepairPickupStatus.Ready) continue;
                if (dayIndex - record.ReadyDayIndex < Math.Max(1, unclaimedDays)) continue;
                record.Status = RepairPickupStatus.Unclaimed;
                flagged.Add(record.RecordId);
                diagnostics.Add(
                    $"RepairPickupLedger: {record.RecordId} ({record.AssetDescription}) unclaimed " +
                    $"after {dayIndex - record.ReadyDayIndex} days — flagged Unclaimed, nothing seized or sold.");
            }
            return flagged;
        }

        // ---------- save DTO (inside the owning ledger class) ----------

        [Serializable]
        public sealed class RepairPickupLedgerDto
        {
            public List<RepairPickupRecord> Records = new List<RepairPickupRecord>();
            public int NextNumber;
        }

        public RepairPickupLedgerDto ToSaveDto()
        {
            var dto = new RepairPickupLedgerDto { NextNumber = nextNumber };
            dto.Records.AddRange(records);
            return dto;
        }

        public void LoadFromSaveDto(RepairPickupLedgerDto dto)
        {
            records.Clear();
            nextNumber = 1;
            if (dto == null) return;
            foreach (RepairPickupRecord r in dto.Records)
                if (r != null) records.Add(r);
            nextNumber = Math.Max(1, dto.NextNumber);
        }
    }

    // ================= artisan's lien =================

    /// <summary>D4K: lifecycle of one artisan's-lien claim.</summary>
    public enum ArtisanLienStatus
    {
        Unspecified = 0,
        Recorded = 1,       // claim filed; the shop retains the item until paid
        Enforceable = 2,    // notice served after the window; operator MAY sell
        Released = 3,       // paid in full — claim discharged
        SatisfiedBySale = 4,// operator sold under the lien; proceeds distributed
    }

    /// <summary>
    /// D4K: one artisan's-lien claim. Research ground: at common law an
    /// artisan who bestows labor/materials on a chattel may RETAIN it until
    /// paid (possessory lien — possession is the lien; release the item and
    /// the lien dies). The 1888 statutory shape added a sale power: after
    /// 3 months unpaid, on 2 weeks' newspaper notice, sell the chattel,
    /// apply proceeds to the debt + advertising/sale costs, and return the
    /// surplus to the owner. This class records the CLAIM — it never
    /// auto-seizes and never auto-sells; sale is an explicit operator action.
    /// </summary>
    [Serializable]
    public sealed class ArtisanLienClaim
    {
        public string ClaimId = string.Empty; // AL-0001
        public string WorkOrderId = string.Empty;
        public string AssetId = string.Empty;
        public string AssetDescription = string.Empty;
        public string ShopBusinessId = string.Empty;
        public string ShopBusinessName = string.Empty;
        public string DebtorName = string.Empty;
        public string DebtorBusinessId = string.Empty;
        public int ClaimAmountCents;
        public int UnpaidBalanceCents;
        public int ClaimedDayIndex;
        public int NoticeGivenDayIndex = -1;
        public int ReleasedDayIndex = -1;
        public ArtisanLienStatus Status = ArtisanLienStatus.Unspecified;
    }

    /// <summary>
    /// D4K: the shop's lien register. Claims are recorded when repair work
    /// completes UNPAID; paying releases the claim. Enforcement is a ladder
    /// of EXPLICIT operator actions (notice, then sale) — the register never
    /// sells on its own, and a scan never disposes of anything.
    /// </summary>
    public sealed class ArtisanLienRegister
    {
        /// <summary>
        /// Research-gated default: 90 days unclaimed before notice. The 1888
        /// shape was 3 months after the bill fell due. Calibration, not canon.
        /// </summary>
        public const int DefaultLienNoticeDays = 90;

        /// <summary>
        /// Research-gated default: 14 days' public notice before a lien sale.
        /// The 1888 shape required 2 weeks' newspaper advertisement.
        /// Calibration, not canon.
        /// </summary>
        public const int DefaultSaleNoticeDays = 14;

        private readonly List<ArtisanLienClaim> claims = new List<ArtisanLienClaim>();
        private int nextNumber = 1;

        public IReadOnlyList<ArtisanLienClaim> Claims => claims;

        public ArtisanLienClaim FindOpenByOrder(string workOrderId)
        {
            foreach (ArtisanLienClaim c in claims)
            {
                if (!string.Equals(c.WorkOrderId, workOrderId, StringComparison.Ordinal)) continue;
                if (c.Status == ArtisanLienStatus.Recorded || c.Status == ArtisanLienStatus.Enforceable)
                    return c;
            }
            return null;
        }

        public ArtisanLienClaim Find(string claimId)
        {
            foreach (ArtisanLienClaim c in claims)
                if (string.Equals(c.ClaimId, claimId, StringComparison.Ordinal))
                    return c;
            return null;
        }

        /// <summary>
        /// Records the lien when repair work completes unpaid: the shop keeps
        /// the item (possession IS the lien) and the debt is on record.
        /// Refuses zero/negative bills (no debt, no lien) and duplicate open
        /// claims on one order.
        /// </summary>
        public ArtisanLienClaim RecordClaim(
            RepairWorkOrder order, string shopBusinessId, string shopBusinessName,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (order == null)
            {
                diagnostics.Add("ArtisanLienRegister: no work order — no claim.");
                return null;
            }
            if (order.Status != RepairOrderStatus.Complete)
            {
                diagnostics.Add(
                    $"ArtisanLienRegister: {order.WorkOrderId} is {order.Status}, not Complete — " +
                    "a lien secures finished work only.");
                return null;
            }
            if (order.AgreedPriceCents <= 0)
            {
                diagnostics.Add(
                    $"ArtisanLienRegister: {order.WorkOrderId} has no agreed price — no debt, no lien.");
                return null;
            }
            if (FindOpenByOrder(order.WorkOrderId) != null)
            {
                diagnostics.Add(
                    $"ArtisanLienRegister: {order.WorkOrderId} already has an open lien claim.");
                return FindOpenByOrder(order.WorkOrderId);
            }
            var claim = new ArtisanLienClaim
            {
                ClaimId = $"AL-{nextNumber++:D4}",
                WorkOrderId = order.WorkOrderId,
                AssetId = order.AssetId,
                AssetDescription = order.AssetDescription,
                ShopBusinessId = shopBusinessId ?? string.Empty,
                ShopBusinessName = shopBusinessName ?? string.Empty,
                DebtorName = order.CustomerName,
                DebtorBusinessId = order.CustomerBusinessId,
                ClaimAmountCents = order.AgreedPriceCents,
                UnpaidBalanceCents = order.AgreedPriceCents,
                ClaimedDayIndex = dayIndex,
                Status = ArtisanLienStatus.Recorded,
            };
            claims.Add(claim);
            diagnostics.Add(
                $"ArtisanLienRegister: {claim.ClaimId} recorded — {claim.ClaimAmountCents}c secured on " +
                $"'{claim.AssetDescription}' (order {order.WorkOrderId}). The shop retains the item until paid; " +
                "this is a recorded claim, never an auto-seizure.");
            return claim;
        }

        /// <summary>Books a payment against the claim; full payment releases it.</summary>
        public string RecordPayment(
            string claimId, int amountCents, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            ArtisanLienClaim claim = Find(claimId);
            if (claim == null) return $"ArtisanLienRegister: unknown claim '{claimId}'.";
            if (claim.Status != ArtisanLienStatus.Recorded && claim.Status != ArtisanLienStatus.Enforceable)
                return $"ArtisanLienRegister: {claimId} is {claim.Status} — no open balance.";
            if (amountCents <= 0) return $"ArtisanLienRegister: payment must be positive.";
            claim.UnpaidBalanceCents = Math.Max(0, claim.UnpaidBalanceCents - amountCents);
            if (claim.UnpaidBalanceCents == 0)
            {
                claim.Status = ArtisanLienStatus.Released;
                claim.ReleasedDayIndex = dayIndex;
                diagnostics.Add(
                    $"ArtisanLienRegister: {claimId} paid in full — claim RELEASED on day {dayIndex}.");
            }
            else
            {
                diagnostics.Add(
                    $"ArtisanLienRegister: {claimId} — {amountCents}c paid, {claim.UnpaidBalanceCents}c still owed.");
            }
            return null;
        }

        /// <summary>
        /// The operator serves enforcement notice after the unclaimed window:
        /// the claim becomes Enforceable — the operator MAY sell, nothing
        /// happens automatically. Requires the item to still be unclaimed
        /// (the caller passes the pickup ledger's finding).
        /// </summary>
        public string ServeEnforcementNotice(
            string claimId, bool itemUnclaimed, int dayIndex, List<string> diagnostics,
            int lienNoticeDays = DefaultLienNoticeDays)
        {
            diagnostics = diagnostics ?? new List<string>();
            ArtisanLienClaim claim = Find(claimId);
            if (claim == null) return $"ArtisanLienRegister: unknown claim '{claimId}'.";
            if (claim.Status != ArtisanLienStatus.Recorded)
                return $"ArtisanLienRegister: {claimId} is {claim.Status} — notice only on a recorded claim.";
            if (!itemUnclaimed)
                return $"ArtisanLienRegister: {claimId} — the item is not unclaimed; no enforcement notice.";
            int age = dayIndex - claim.ClaimedDayIndex;
            if (age < Math.Max(1, lienNoticeDays))
                return $"ArtisanLienRegister: {claimId} — only {age} days since the claim; " +
                       $"notice needs {lienNoticeDays} (research-gated 1888 shape).";
            claim.Status = ArtisanLienStatus.Enforceable;
            claim.NoticeGivenDayIndex = dayIndex;
            diagnostics.Add(
                $"ArtisanLienRegister: {claimId} ENFORCEABLE — notice served day {dayIndex} after {age} days. " +
                "The operator may sell after the notice period; nothing is sold automatically.");
            return null;
        }

        /// <summary>
        /// The operator's EXPLICIT lien sale (never automatic): proceeds pay
        /// the claim balance + sale costs first, and the SURPLUS returns to
        /// the debtor — the 1888 statutory shape. Refuses claims that are not
        /// Enforceable, and refuses before the notice period has run.
        /// </summary>
        public string RecordLienSale(
            string claimId, int salePriceCents, int saleCostsCents, string buyerName,
            int dayIndex, List<string> diagnostics,
            out int shopShareCents, out int surplusToOwnerCents,
            int saleNoticeDays = DefaultSaleNoticeDays)
        {
            diagnostics = diagnostics ?? new List<string>();
            shopShareCents = 0;
            surplusToOwnerCents = 0;
            ArtisanLienClaim claim = Find(claimId);
            if (claim == null) return $"ArtisanLienRegister: unknown claim '{claimId}'.";
            if (claim.Status != ArtisanLienStatus.Enforceable)
                return $"ArtisanLienRegister: {claimId} is {claim.Status} — sale only on an enforceable claim " +
                       "(serve notice first).";
            if (dayIndex - claim.NoticeGivenDayIndex < Math.Max(1, saleNoticeDays))
                return $"ArtisanLienRegister: {claimId} — notice period ({saleNoticeDays} days) has not run.";
            if (salePriceCents <= 0) return $"ArtisanLienRegister: sale price must be positive.";
            if (string.IsNullOrWhiteSpace(buyerName))
                return $"ArtisanLienRegister: a lien sale needs a named buyer — no anonymous sales.";
            int due = claim.UnpaidBalanceCents + Math.Max(0, saleCostsCents);
            shopShareCents = Math.Min(Math.Max(0, salePriceCents), due);
            surplusToOwnerCents = Math.Max(0, salePriceCents) - shopShareCents;
            claim.UnpaidBalanceCents = Math.Max(0, due - shopShareCents);
            claim.Status = ArtisanLienStatus.SatisfiedBySale;
            claim.ReleasedDayIndex = dayIndex;
            diagnostics.Add(
                $"ArtisanLienRegister: {claimId} sold to {buyerName} for {salePriceCents}c on day {dayIndex} — " +
                $"shop takes {shopShareCents}c (claim + {Math.Max(0, saleCostsCents)}c costs), " +
                $"surplus {surplusToOwnerCents}c returns to {claim.DebtorName}.");
            return null;
        }

        // ---------- save DTO (inside the owning register class) ----------

        [Serializable]
        public sealed class ArtisanLienRegisterDto
        {
            public List<ArtisanLienClaim> Claims = new List<ArtisanLienClaim>();
            public int NextNumber;
        }

        public ArtisanLienRegisterDto ToSaveDto()
        {
            var dto = new ArtisanLienRegisterDto { NextNumber = nextNumber };
            dto.Claims.AddRange(claims);
            return dto;
        }

        public void LoadFromSaveDto(ArtisanLienRegisterDto dto)
        {
            claims.Clear();
            nextNumber = 1;
            if (dto == null) return;
            foreach (ArtisanLienClaim c in dto.Claims)
                if (c != null) claims.Add(c);
            nextNumber = Math.Max(1, dto.NextNumber);
        }
    }
}
