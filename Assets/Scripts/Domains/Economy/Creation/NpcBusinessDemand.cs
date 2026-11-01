using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// Phase G (G5): what a business's demand is FOR. Append-only.
    /// Businesses need inputs, equipment, maintenance, labor, freight,
    /// fuel, buildings and working capital — and they buy them through
    /// the real procurement authorities, as customers.
    /// </summary>
    public enum NpcBusinessDemandPurpose
    {
        Unspecified = 0,
        Inputs = 1,
        Equipment = 2,
        Maintenance = 3,
        Labor = 4,
        Freight = 5,
        Fuel = 6,
        Buildings = 7,
        WorkingCapital = 8,
    }

    /// <summary>
    /// Phase G (G5): one input need of a business for a week — derived
    /// from the business's declared activities, never manufactured to hit
    /// a target. A bakery needs flour because it bakes; it does not need
    /// flour because a quota says so.
    /// </summary>
    [Serializable]
    public sealed class NpcBusinessInputNeed
    {
        public string BusinessInstanceId = string.Empty;
        public NpcBusinessDemandPurpose Purpose = NpcBusinessDemandPurpose.Unspecified;
        public string ItemId = string.Empty;
        public int UnitsPerWeek;
        public int MaxPriceCentsPerUnit;
        public string DerivedFromActivity = string.Empty;

        public NpcBusinessInputNeed() { }
    }

    /// <summary>
    /// Phase G (G5): one executed business purchase — real supplier, real
    /// cash, real provenance. This is legitimate demand, not manufactured:
    /// the port only buys from real suppliers through the procurement
    /// authorities.
    /// </summary>
    [Serializable]
    public sealed class NpcBusinessPurchase
    {
        public string BusinessInstanceId = string.Empty;
        public string ItemId = string.Empty;
        public int Units;
        public int TotalCostCents;
        public string SupplierId = string.Empty;
        public string Provenance = string.Empty;
        public int DayIndex;

        public NpcBusinessPurchase() { }
    }

    /// <summary>
    /// Phase G (G5): the port to real procurement. The Unity runtime
    /// implements this over the procurement authorities (business as
    /// customer, same shopping-loop mechanics as households where
    /// appropriate); tests use a fake supplier.
    /// </summary>
    public interface INpcBusinessProcurementPort
    {
        /// <summary>
        /// Buys from a REAL supplier. Returns null on success with the
        /// purchase recorded; a loud refusal (no supplier, no stock, no
        /// cash) otherwise. Never invents supply.
        /// </summary>
        string PurchaseForBusiness(string businessInstanceId, string itemId, int units,
            int maxPriceCentsPerUnit, int dayIndex, out NpcBusinessPurchase purchase);
    }

    /// <summary>
    /// Phase G (G5): plans and executes a business's real demand. Needs are
    /// declared per activity (what the work actually consumes); the
    /// planner turns them into purchases through the procurement port.
    /// There is deliberately no "create demand" path — unmet needs stay
    /// visible as unmet, which is itself the opportunity signal for G1.
    /// </summary>
    public sealed class NpcBusinessDemandPlanner
    {
        /// <summary>
        /// Declares one week of input needs from the business's activities.
        /// Each need names the activity it derives from — traceable, never
        /// a quota.
        /// </summary>
        public List<NpcBusinessInputNeed> PlanWeek(
            string businessInstanceId,
            IEnumerable<NpcBusinessInputNeed> standingNeeds,
            NpcBusinessEventLog events,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var planned = new List<NpcBusinessInputNeed>();
            if (standingNeeds == null) return planned;
            foreach (NpcBusinessInputNeed need in standingNeeds)
            {
                if (need == null || string.IsNullOrWhiteSpace(need.ItemId) || need.UnitsPerWeek <= 0)
                {
                    continue;
                }

                planned.Add(new NpcBusinessInputNeed
                {
                    BusinessInstanceId = businessInstanceId ?? string.Empty,
                    Purpose = need.Purpose,
                    ItemId = need.ItemId,
                    UnitsPerWeek = need.UnitsPerWeek,
                    MaxPriceCentsPerUnit = need.MaxPriceCentsPerUnit,
                    DerivedFromActivity = need.DerivedFromActivity,
                });
            }

            events?.Record(dayIndex, NpcBusinessEventKind.DemandPlanned, -1, businessInstanceId ?? string.Empty,
                $"{businessInstanceId} planned {planned.Count} input need(s) for the week, " +
                "each derived from a declared activity — legitimate demand, not manufactured.");
            diagnostics.Add($"{businessInstanceId}: planned {planned.Count} input need(s).");
            return planned;
        }

        /// <summary>
        /// Executes the planned purchases through the procurement port.
        /// Cash leaves the business (conserved); goods arrive with
        /// provenance. Needs the port cannot fill stay unmet and visible.
        /// </summary>
        public NpcBusinessDemandResult ExecutePurchases(
            IEnumerable<NpcBusinessInputNeed> plannedNeeds,
            INpcBusinessProcurementPort procurement,
            NpcBusinessEventLog events,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var result = new NpcBusinessDemandResult();
            if (plannedNeeds == null || procurement == null)
            {
                result.Notes.Add("Demand execution needs planned needs and a procurement port.");
                return result;
            }

            foreach (NpcBusinessInputNeed need in plannedNeeds)
            {
                if (need == null) continue;
                string refusal = procurement.PurchaseForBusiness(
                    need.BusinessInstanceId, need.ItemId, need.UnitsPerWeek,
                    need.MaxPriceCentsPerUnit, dayIndex, out NpcBusinessPurchase purchase);
                if (refusal == null && purchase != null)
                {
                    result.Purchases.Add(purchase);
                    result.TotalSpentCents += purchase.TotalCostCents;
                    events?.Record(dayIndex, NpcBusinessEventKind.DemandPurchased, -1,
                        need.BusinessInstanceId,
                        $"{need.BusinessInstanceId} bought {purchase.Units}x '{purchase.ItemId}' from " +
                        $"'{purchase.SupplierId}' for {purchase.TotalCostCents}c ({need.Purpose}; provenance '{purchase.Provenance}').");
                }
                else
                {
                    result.UnmetNeeds.Add(need);
                    result.Notes.Add($"unmet: {need.UnitsPerWeek}x '{need.ItemId}' ({need.Purpose}) — {refusal ?? "no purchase"}; stays visible as unmet demand.");
                }
            }

            diagnostics.Add($"demand executed: {result.Purchases.Count} purchase(s), {result.TotalSpentCents}c spent, {result.UnmetNeeds.Count} unmet.");
            return result;
        }
    }

    /// <summary>Phase G (G5): the outcome of one demand execution pass.</summary>
    [Serializable]
    public sealed class NpcBusinessDemandResult
    {
        public List<NpcBusinessPurchase> Purchases = new List<NpcBusinessPurchase>();
        public List<NpcBusinessInputNeed> UnmetNeeds = new List<NpcBusinessInputNeed>();
        public int TotalSpentCents;
        public List<string> Notes = new List<string>();

        public NpcBusinessDemandResult() { }
    }
}
