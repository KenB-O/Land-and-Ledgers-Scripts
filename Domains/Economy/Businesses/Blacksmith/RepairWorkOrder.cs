using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Blacksmith
{
    /// <summary>Lifecycle of one repair job (Canon §7.2C work orders, §7.2D queues).</summary>
    public enum RepairOrderStatus
    {
        Unspecified = 0,
        Intake = 1,      // reported, awaiting diagnosis
        Diagnosed = 2,   // actual problem known, materials estimated
        Quoted = 3,      // price/terms agreed with the customer
        InProgress = 4,  // labor being performed
        Complete = 5,    // work done, awaiting payment
        Paid = 6,
    }

    public enum RepairUrgency
    {
        Routine = 0,
        Urgent = 1,
        Emergency = 2, // jumps the queue (Canon §7.2: interrupt-driven)
    }

    /// <summary>
    /// EQU-2: one repair work order. Canon §7.2C fields: customer, asset/component,
    /// reported problem, actual/diagnosed problem, urgency, required capability,
    /// materials, location, queue position, estimated labor, agreed price/terms.
    /// Physical truth holds: the asset or the repairperson travels — nothing
    /// teleports to the shop.
    /// </summary>
    [Serializable]
    public sealed class RepairWorkOrder
    {
        public string WorkOrderId = string.Empty;
        public string CustomerName = string.Empty;
        public string CustomerBusinessId = string.Empty;
        public string AssetId = string.Empty; // the EquipmentAsset under repair
        public string AssetDescription = string.Empty;
        public string ReportedProblem = string.Empty;
        public string DiagnosedProblem = string.Empty;
        public RepairUrgency Urgency = RepairUrgency.Routine;
        public string RequiredCapability = string.Empty; // "smithing", "farrier", "wagon-work"
        public int MaterialUnitsNeeded;
        public string MaterialId = string.Empty;
        public string LocationId = string.Empty; // where the asset IS — physical truth
        public int QueuePosition;
        public int EstimatedLaborMinutes;
        public int AgreedPriceCents;
        public bool TermsAgreed;
        public RepairOrderStatus Status = RepairOrderStatus.Unspecified;
        public int OpenedDayIndex;
        public int CompletedDayIndex = -1;
    }

    /// <summary>
    /// EQU-2: the smith's real job queue. Jobs compete for skilled labor, workstations,
    /// material and time (Canon §7.2D). Emergency work interrupts the queue.
    /// </summary>
    public sealed class RepairQueue
    {
        private readonly List<RepairWorkOrder> orders = new List<RepairWorkOrder>();
        private int nextNumber = 1;

        public IReadOnlyList<RepairWorkOrder> Orders => orders;

        public RepairWorkOrder Intake(string customerName, string customerBusinessId,
            string assetId, string assetDescription, string reportedProblem,
            string requiredCapability, string locationId, RepairUrgency urgency,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var order = new RepairWorkOrder
            {
                WorkOrderId = $"WO-{nextNumber++:D4}",
                CustomerName = customerName ?? string.Empty,
                CustomerBusinessId = customerBusinessId ?? string.Empty,
                AssetId = assetId ?? string.Empty,
                AssetDescription = assetDescription ?? string.Empty,
                ReportedProblem = reportedProblem ?? string.Empty,
                RequiredCapability = requiredCapability ?? string.Empty,
                LocationId = locationId ?? string.Empty,
                Urgency = urgency,
                Status = RepairOrderStatus.Intake,
                OpenedDayIndex = dayIndex,
            };
            orders.Add(order);
            RecomputeQueuePositions();
            diagnostics.Add($"RepairQueue: work order {order.WorkOrderId} opened for '{order.AssetDescription}' ({urgency}).");
            return order;
        }

        /// <summary>
        /// Diagnosis can reveal additional work and convert a rough estimate into a
        /// proper quote (Canon §7.2D).
        /// </summary>
        public string Diagnose(string workOrderId, string diagnosedProblem,
            int materialUnitsNeeded, string materialId, int estimatedLaborMinutes)
        {
            RepairWorkOrder order = Find(workOrderId);
            if (order == null) return $"RepairQueue: unknown work order '{workOrderId}'.";
            if (order.Status != RepairOrderStatus.Intake)
                return $"RepairQueue: {workOrderId} is {order.Status}, not Intake.";
            order.DiagnosedProblem = diagnosedProblem ?? string.Empty;
            order.MaterialUnitsNeeded = Math.Max(0, materialUnitsNeeded);
            order.MaterialId = materialId ?? string.Empty;
            order.EstimatedLaborMinutes = Math.Max(0, estimatedLaborMinutes);
            order.Status = RepairOrderStatus.Diagnosed;
            return null;
        }

        /// <summary>Pricing freedom: any price the market can plausibly accept (Canon §7.2D).</summary>
        public string AgreeTerms(string workOrderId, int agreedPriceCents)
        {
            RepairWorkOrder order = Find(workOrderId);
            if (order == null) return $"RepairQueue: unknown work order '{workOrderId}'.";
            if (order.Status != RepairOrderStatus.Diagnosed)
                return $"RepairQueue: {workOrderId} is {order.Status}, not Diagnosed.";
            if (agreedPriceCents < 0)
                return $"RepairQueue: price cannot be negative.";
            order.AgreedPriceCents = agreedPriceCents;
            order.TermsAgreed = true;
            order.Status = RepairOrderStatus.Quoted;
            return null;
        }

        private RepairWorkOrder Find(string workOrderId)
        {
            foreach (RepairWorkOrder o in orders)
                if (string.Equals(o.WorkOrderId, workOrderId, StringComparison.Ordinal))
                    return o;
            return null;
        }

        private void RecomputeQueuePositions()
        {
            // Emergency first, then urgent, then routine — FIFO within urgency.
            var sorted = new List<RepairWorkOrder>(orders);
            sorted.Sort((a, b) =>
            {
                int u = ((int)b.Urgency).CompareTo((int)a.Urgency);
                return u != 0 ? u : a.OpenedDayIndex.CompareTo(b.OpenedDayIndex);
            });
            for (int i = 0; i < sorted.Count; i++)
                sorted[i].QueuePosition = i + 1;
        }
    }
}
