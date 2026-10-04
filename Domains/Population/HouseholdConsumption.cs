using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// HF-4: a household procurement need (Canon XIII 13.4). Needs are a PLANNING source
    /// only: they never post revenue or move goods by themselves. Execution must go
    /// through an acting Person via <see cref="IEmbodiedPurchaseExecutor"/>
    /// (ProcurementNeed -> acting Person/channel -> journey -> transaction).
    /// NeedSequence is planning-local; only executed transactions persist.
    /// </summary>
    [Serializable]
    public sealed class ProcurementNeed
    {
        public int NeedSequence;
        public int HouseholdId;
        public string CategoryId = string.Empty;
        public int UnitsNeeded;
        public float Priority01;
        public int RequestingPersonId = -1;
        public string Reason = string.Empty;
        public int DayIndex;
    }

    /// <summary>
    /// Result of executing one procurement need through an acting Person.
    /// </summary>
    [Serializable]
    public sealed class PurchaseExecutionResult
    {
        public int NeedSequence;
        public bool Success;
        public int ActingPersonId = -1;
        public int UnitsAcquired;
        public int AmountPaidCents;
        public string Counterparty = string.Empty;
        public string Notes = string.Empty;
    }

    /// <summary>
    /// HF-4: the embodied-purchasing execution contract (Canon 13.4). Implementations must
    /// resolve an acting Person, a channel/journey, supplier availability and a real
    /// transaction — opening a store or raising aggregate demand never creates a customer.
    /// </summary>
    public interface IEmbodiedPurchaseExecutor
    {
        PurchaseExecutionResult Execute(ProcurementNeed need, int actingPersonId, int dayIndex);
    }

    /// <summary>
    /// HF-4: builds procurement plans from household state. Reads reserves, the demand
    /// snapshot and member count; emits needs. Planning only — no revenue, no goods
    /// movement, no synthetic demand (stale-doctrine flags: no anonymous buyers).
    /// </summary>
    public sealed class HouseholdConsumptionPlanner
    {
        private int nextNeedSequence;
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Plans procurement needs for a household: low reserves, demand-snapshot gaps and
        /// per-member staples. Returns needs only; the caller executes them through an
        /// <see cref="IEmbodiedPurchaseExecutor"/>.
        /// </summary>
        public List<ProcurementNeed> BuildPlan(HouseholdState household, int requestingPersonId, int dayIndex)
        {
            var needs = new List<ProcurementNeed>();
            if (household == null)
            {
                return needs;
            }

            // 1. Reserve shortfalls: categories below their low threshold need replenishment.
            if (household.reserves != null)
            {
                for (int i = 0; i < household.reserves.Count; i++)
                {
                    HouseholdReserveState reserve = household.reserves[i];
                    if (reserve == null || string.IsNullOrWhiteSpace(reserve.categoryId))
                    {
                        continue;
                    }

                    int shortfall = Math.Max(0, reserve.lowThresholdUnits - Math.Max(0, reserve.currentUnits));
                    if (shortfall > 0)
                    {
                        needs.Add(NewNeed(
                            household.id,
                            reserve.categoryId,
                            shortfall,
                            0.8f,
                            requestingPersonId,
                            $"reserve '{reserve.categoryId}' below low threshold ({reserve.currentUnits}/{reserve.lowThresholdUnits})",
                            dayIndex));
                    }
                }
            }

            // 2. Demand-snapshot gaps: unmet need the household already tracks.
            // P2: category ids must match HouseholdReserveCatalog exactly —
            // "general_goods"/"medicine"/"heating_fuel" were phantom ids no
            // reserve ever held, so purchases credited to them vanished
            // untracked (EmbodiedPurchaseExecutor logs "no reserve tracked").
            HouseholdDemandSnapshot demand = household.demandSnapshot;
            AddDemandNeed(needs, household.id, "staple_food", demand.foodNeed, requestingPersonId, dayIndex);
            AddDemandNeed(needs, household.id, "household_goods", demand.generalGoodsNeed, requestingPersonId, dayIndex);
            AddDemandNeed(needs, household.id, "medicine_remedies", demand.medicineNeed, requestingPersonId, dayIndex);
            AddDemandNeed(needs, household.id, HouseholdReserveCatalog.FuelWoodCategoryId, demand.heatingNeed, requestingPersonId, dayIndex);

            return needs;
        }

        private void AddDemandNeed(
            List<ProcurementNeed> needs,
            int householdId,
            string categoryId,
            int needUnits,
            int requestingPersonId,
            int dayIndex)
        {
            if (needUnits <= 0)
            {
                return;
            }

            needs.Add(NewNeed(
                householdId,
                categoryId,
                needUnits,
                0.5f,
                requestingPersonId,
                $"demand snapshot tracks {needUnits} unmet units of '{categoryId}'",
                dayIndex));
        }

        private ProcurementNeed NewNeed(
            int householdId,
            string categoryId,
            int units,
            float priority01,
            int requestingPersonId,
            string reason,
            int dayIndex)
        {
            return new ProcurementNeed
            {
                NeedSequence = nextNeedSequence++,
                HouseholdId = householdId,
                CategoryId = categoryId,
                UnitsNeeded = Math.Max(0, units),
                Priority01 = Math.Min(1f, Math.Max(0f, priority01)),
                RequestingPersonId = requestingPersonId,
                Reason = reason ?? string.Empty,
                DayIndex = dayIndex,
            };
        }
    }
}
