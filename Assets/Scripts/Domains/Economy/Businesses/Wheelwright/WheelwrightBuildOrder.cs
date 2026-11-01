using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Wheelwright
{
    /// <summary>
    /// D3A: lifecycle of one CUSTOM wagon/wheel build order — the fabrication
    /// counterpart to the repair work-order lifecycle (Canon §7.2C/D). Canon
    /// describes intake → diagnose → quote → agree → complete for REPAIR work
    /// orders; the wagon maker (1878 Deadwood directory evidence, Canon §7.2A)
    /// sells new wagons the same way: a customer commissions a build, the shop
    /// quotes its ask (parts + labor, pricing freedom Canon §7.2D), terms are
    /// agreed, and the build queues for bench time. Rush orders are the Canon
    /// §9.3 emergency lever: they cost more and jump the non-rush queue
    /// (operator policy, calibration — not a canon constant).
    ///
    /// Single-unit orders only (one wagon per order — D3A scope). A freight
    /// company wanting three wagons places three orders; each wagon keeps its
    /// own provenance and promised day.
    /// </summary>
    public enum WheelwrightBuildOrderStatus
    {
        Unspecified = 0,
        Intake = 1,    // customer asked for a build; not yet quoted
        Quoted = 2,    // the shop's ask is on the table
        Agreed = 3,    // price/terms agreed — queued for bench time
        InProgress = 4, // materials being consumed / wagon under construction
        Complete = 5,  // built and handed over
        Cancelled = 6, // withdrawn before completion; never built
    }

    /// <summary>D3A: one custom build order (serializable — lives in the shop's save DTO).</summary>
    [Serializable]
    public sealed class WheelwrightBuildOrder
    {
        public string OrderId = string.Empty;          // BO-0001
        public string CustomerName = string.Empty;
        public string CustomerBusinessId = string.Empty;
        public string RecipeKind = string.Empty;       // "freight-wagon", ...
        public string RecipeDisplayName = string.Empty;
        public bool IsRush;                            // Canon §9.3 emergency lever
        public WheelwrightBuildOrderStatus Status = WheelwrightBuildOrderStatus.Unspecified;
        public int QuotedPriceCents;
        public int AgreedPriceCents;
        public bool TermsAgreed;
        public int EstimatedLaborMinutes;
        public int QueuePosition;                      // among Agreed orders; rush-first, FIFO
        public int PromisedDayIndex = -1;              // set when terms are agreed
        public int OpenedDayIndex;
        public int CompletedDayIndex = -1;
        /// <summary>D3A: the EquipmentAsset built to satisfy this order (set on completion).</summary>
        public string BuiltAssetId = string.Empty;

        public bool IsOpen =>
            Status != WheelwrightBuildOrderStatus.Complete
            && Status != WheelwrightBuildOrderStatus.Cancelled;
    }

    /// <summary>
    /// D3A: the shop's custom-build order book. Pure order state machine — it
    /// knows nothing about lumber or ironwork; the WheelwrightRuntime pulls
    /// Agreed orders off the book and performs the actual builds (materials,
    /// skill, station gates live there).
    /// </summary>
    public sealed class WheelwrightBuildOrderBook
    {
        /// <summary>Calibration: rush jobs bill labor at a premium. Tuning, not canon.</summary>
        public const float RushLaborPremium = 1.25f;

        private readonly List<WheelwrightBuildOrder> orders = new List<WheelwrightBuildOrder>();
        private int nextNumber = 1;

        public IReadOnlyList<WheelwrightBuildOrder> Orders => orders;
        public int NextOrderNumber => nextNumber;

        public void SetNextOrderNumber(int value) { nextNumber = Math.Max(1, value); }

        public WheelwrightBuildOrder Find(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId)) return null;
            foreach (WheelwrightBuildOrder o in orders)
                if (string.Equals(o.OrderId, orderId, StringComparison.Ordinal))
                    return o;
            return null;
        }

        /// <summary>
        /// A customer commissions a build. Refuses unknown recipe kinds loudly —
        /// the book only sells what the shop's recipes can actually build.
        /// </summary>
        public WheelwrightBuildOrder RequestBuild(
            string customerName, string customerBusinessId,
            string recipeKind, bool isRush, int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            WheelwrightBuildRecipe recipe = FindRecipe(recipeKind);
            if (recipe == null)
            {
                diagnostics.Add($"WheelwrightBuildOrderBook: no build recipe for '{recipeKind}' — order refused.");
                return null;
            }
            var order = new WheelwrightBuildOrder
            {
                OrderId = $"BO-{nextNumber++:D4}",
                CustomerName = customerName ?? string.Empty,
                CustomerBusinessId = customerBusinessId ?? string.Empty,
                RecipeKind = recipe.EquipmentKind,
                RecipeDisplayName = recipe.DisplayName,
                IsRush = isRush,
                Status = WheelwrightBuildOrderStatus.Intake,
                EstimatedLaborMinutes = recipe.LaborMinutes,
                OpenedDayIndex = dayIndex,
            };
            orders.Add(order);
            diagnostics.Add(
                $"WheelwrightBuildOrderBook: {order.OrderId} opened — {recipe.DisplayName} for {order.CustomerName}" +
                (isRush ? " (RUSH)." : "."));
            return order;
        }

        /// <summary>Puts the shop's ask on the table: parts + labor, pricing freedom (Canon §7.2D).</summary>
        public string QuoteBuild(string orderId, int quotedPriceCents, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            WheelwrightBuildOrder order = Find(orderId);
            if (order == null) return $"WheelwrightBuildOrderBook: unknown build order '{orderId}'.";
            if (order.Status != WheelwrightBuildOrderStatus.Intake)
                return $"WheelwrightBuildOrderBook: {orderId} is {order.Status}, not Intake.";
            if (quotedPriceCents <= 0) return $"WheelwrightBuildOrderBook: quote must be positive.";
            order.QuotedPriceCents = quotedPriceCents;
            order.Status = WheelwrightBuildOrderStatus.Quoted;
            diagnostics.Add($"WheelwrightBuildOrderBook: {orderId} quoted at {quotedPriceCents}c.");
            return null;
        }

        /// <summary>
        /// Customer accepts the ask (or haggles to an agreed figure — pricing
        /// freedom cuts both ways, Canon §7.2D). Agreed orders join the bench
        /// queue; the promised day is estimated from real queue labor.
        /// </summary>
        public string AgreeBuildTerms(string orderId, int agreedPriceCents, int currentDayIndex,
            int shopLaborMinutesPerDay, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            WheelwrightBuildOrder order = Find(orderId);
            if (order == null) return $"WheelwrightBuildOrderBook: unknown build order '{orderId}'.";
            if (order.Status != WheelwrightBuildOrderStatus.Quoted)
                return $"WheelwrightBuildOrderBook: {orderId} is {order.Status}, not Quoted.";
            if (agreedPriceCents <= 0) return $"WheelwrightBuildOrderBook: agreed price must be positive.";
            order.AgreedPriceCents = agreedPriceCents;
            order.TermsAgreed = true;
            order.Status = WheelwrightBuildOrderStatus.Agreed;
            RecomputeQueuePositions();
            order.PromisedDayIndex = EstimatePromisedDay(order, currentDayIndex, shopLaborMinutesPerDay);
            diagnostics.Add(
                $"WheelwrightBuildOrderBook: {orderId} agreed at {agreedPriceCents}c — " +
                $"queue #{order.QueuePosition}, promised day {order.PromisedDayIndex}.");
            return null;
        }

        public string CancelBuild(string orderId, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            WheelwrightBuildOrder order = Find(orderId);
            if (order == null) return $"WheelwrightBuildOrderBook: unknown build order '{orderId}'.";
            if (order.Status == WheelwrightBuildOrderStatus.Complete)
                return $"WheelwrightBuildOrderBook: {orderId} is already complete — nothing to cancel.";
            if (order.Status == WheelwrightBuildOrderStatus.InProgress)
                return $"WheelwrightBuildOrderBook: {orderId} is under construction — cancel refuses (materials already committed).";
            order.Status = WheelwrightBuildOrderStatus.Cancelled;
            RecomputeQueuePositions();
            diagnostics.Add($"WheelwrightBuildOrderBook: {orderId} cancelled.");
            return null;
        }

        public string MarkBuildStarted(string orderId)
        {
            WheelwrightBuildOrder order = Find(orderId);
            if (order == null) return $"WheelwrightBuildOrderBook: unknown build order '{orderId}'.";
            if (order.Status != WheelwrightBuildOrderStatus.Agreed)
                return $"WheelwrightBuildOrderBook: {orderId} is {order.Status}, not Agreed.";
            order.Status = WheelwrightBuildOrderStatus.InProgress;
            return null;
        }

        public string MarkBuildComplete(string orderId, int dayIndex)
        {
            WheelwrightBuildOrder order = Find(orderId);
            if (order == null) return $"WheelwrightBuildOrderBook: unknown build order '{orderId}'.";
            if (order.Status != WheelwrightBuildOrderStatus.InProgress)
                return $"WheelwrightBuildOrderBook: {orderId} is {order.Status}, not InProgress.";
            order.Status = WheelwrightBuildOrderStatus.Complete;
            order.CompletedDayIndex = dayIndex;
            return null;
        }

        /// <summary>
        /// Sends an InProgress order back to the bench queue — used when the
        /// build was refused for want of materials or skill. Nothing was
        /// consumed, so the order simply waits for restocking.
        /// </summary>
        public string RequeueBuild(string orderId)
        {
            WheelwrightBuildOrder order = Find(orderId);
            if (order == null) return $"WheelwrightBuildOrderBook: unknown build order '{orderId}'.";
            if (order.Status != WheelwrightBuildOrderStatus.InProgress)
                return $"WheelwrightBuildOrderBook: {orderId} is {order.Status}, not InProgress.";
            order.Status = WheelwrightBuildOrderStatus.Agreed;
            RecomputeQueuePositions();
            return null;
        }

        /// <summary>
        /// Queue order: rush jobs jump ahead of non-rush (Canon §9.3 emergency
        /// lever — operator policy, calibration), FIFO within each tier.
        /// </summary>
        private void RecomputeQueuePositions()
        {
            var queued = new List<WheelwrightBuildOrder>();
            foreach (WheelwrightBuildOrder o in orders)
                if (o.Status == WheelwrightBuildOrderStatus.Agreed)
                    queued.Add(o);
            queued.Sort((a, b) =>
            {
                int rush = b.IsRush.CompareTo(a.IsRush);
                if (rush != 0) return rush;
                int day = a.OpenedDayIndex.CompareTo(b.OpenedDayIndex);
                if (day != 0) return day;
                return string.Compare(a.OrderId, b.OrderId, StringComparison.Ordinal);
            });
            for (int i = 0; i < queued.Count; i++)
                queued[i].QueuePosition = i + 1;
        }

        /// <summary>
        /// Promised completion day from real queue labor ahead plus the order's
        /// own labor, over the shop's daily bench capacity. Mirrors
        /// TurnaroundEstimator (repair side) — one capacity model for the shop.
        /// </summary>
        public int EstimatePromisedDay(WheelwrightBuildOrder order, int currentDayIndex,
            int shopLaborMinutesPerDay)
        {
            if (order == null) return currentDayIndex;
            int daily = Math.Max(1, shopLaborMinutesPerDay);
            int aheadMinutes = 0;
            foreach (WheelwrightBuildOrder o in orders)
            {
                if (ReferenceEquals(o, order)) continue;
                if (o.Status != WheelwrightBuildOrderStatus.Agreed) continue;
                if (o.QueuePosition < order.QueuePosition)
                    aheadMinutes += Math.Max(0, o.EstimatedLaborMinutes);
            }
            int daysBeforeStart = (int)Math.Ceiling(aheadMinutes / (double)daily);
            int ownDays = Math.Max(1, (int)Math.Ceiling(Math.Max(0, order.EstimatedLaborMinutes) / (double)daily));
            return currentDayIndex + daysBeforeStart + ownDays - 1;
        }

        private static WheelwrightBuildRecipe FindRecipe(string recipeKind)
        {
            if (string.IsNullOrWhiteSpace(recipeKind)) return null;
            foreach (WheelwrightBuildRecipe r in WheelwrightRuntime.DefaultRecipes())
                if (string.Equals(r.EquipmentKind, recipeKind, StringComparison.Ordinal))
                    return r;
            return null;
        }

        // ---------- save DTO (inside the owning book class) ----------

        [Serializable]
        public sealed class BuildOrderBookDto
        {
            public List<WheelwrightBuildOrder> Orders = new List<WheelwrightBuildOrder>();
            public int NextOrderNumber = 1;
        }

        public BuildOrderBookDto ToSaveDto()
        {
            var dto = new BuildOrderBookDto { NextOrderNumber = nextNumber };
            dto.Orders.AddRange(orders);
            return dto;
        }

        public void LoadFromSaveDto(BuildOrderBookDto dto)
        {
            orders.Clear();
            if (dto == null) { nextNumber = 1; return; }
            foreach (WheelwrightBuildOrder o in dto.Orders)
                if (o != null && !string.IsNullOrWhiteSpace(o.OrderId))
                    orders.Add(o);
            nextNumber = Math.Max(1, dto.NextOrderNumber);
            RecomputeQueuePositions();
        }
    }
}
