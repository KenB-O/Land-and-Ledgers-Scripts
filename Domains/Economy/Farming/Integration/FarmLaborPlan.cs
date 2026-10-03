using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Integration
{
    /// <summary>Who can work: family (PKG-2), hired hands (PKG-6), or the player (TTS-4).</summary>
    public enum FarmWorkerKind
    {
        Unspecified = 0,
        Family = 1,  // WorkRelationship: operator / family labor, no wage
        Hired = 2,   // EmploymentRelationship: wage per agreed terms
        Player = 3,  // TTS-4 PlayerDirector: the player does it themselves
    }

    [Serializable]
    public sealed class FarmWorker
    {
        public EntityId PersonId = EntityId.Invalid;
        public string Name = string.Empty;
        public FarmWorkerKind Kind;
        public int DailyMinutesAvailable = 480; // TTS-1: against the 24h clock via WorkTimeBudgetStore

        public FarmWorker() { }

        public FarmWorker(EntityId personId, string name, FarmWorkerKind kind, int dailyMinutesAvailable = 480)
        {
            PersonId = personId;
            Name = name ?? string.Empty;
            Kind = kind;
            DailyMinutesAvailable = Math.Max(0, dailyMinutesAvailable);
        }
    }

    [Serializable]
    public sealed class PlannedFarmTask
    {
        public string TaskDefinitionId = string.Empty;
        public string DisplayName = string.Empty;
        public int Minutes;
        public EntityId AssignedWorkerId = EntityId.Invalid;
        public string AssignedWorkerName = string.Empty;
        public string Notes = string.Empty;
    }

    /// <summary>
    /// FVS-4: the daily labor plan for the whole farm loop. Every task in the
    /// chain — milking, feeding, egg collection, churning, delivery handling,
    /// construction — is assigned to a REAL person with a work-time budget.
    /// When demand exceeds supply the plan reports overload (TTS-2 overload
    /// queues); it never invents workers.
    /// </summary>
    public sealed class FarmLaborPlan
    {
        public List<FarmWorker> Workers = new List<FarmWorker>();

        public sealed class DayPlan
        {
            public List<PlannedFarmTask> Tasks = new List<PlannedFarmTask>();
            public List<string> Unassigned = new List<string>();
            public int TotalMinutes;
            public bool Overloaded => Unassigned.Count > 0;
        }

        /// <summary>
        /// Plans one day. Work expands with the herd; assignment is
        /// round-robin across workers with remaining budget.
        /// </summary>
        public DayPlan PlanDay(
            int lactatingCows,
            int totalCattle,
            int hens,
            int churnBatches,
            bool deliveryHandling,
            bool constructionActive,
            int feedHarvestToday)
        {
            var plan = new DayPlan();
            var demand = new List<PlannedFarmTask>();

            if (lactatingCows > 0)
            {
                demand.Add(new PlannedFarmTask
                {
                    TaskDefinitionId = DairyChain.MilkCowTaskId,
                    DisplayName = $"Milk {lactatingCows} cows",
                    Minutes = Math.Max(1, lactatingCows * DairyChain.MilkingMinutesPerHead),
                    Notes = "TTS-2 task per head; 1 XP/min to animal-husbandry (TTS-3).",
                });
            }
            if (totalCattle + hens > 0)
            {
                demand.Add(new PlannedFarmTask
                {
                    TaskDefinitionId = "feed-livestock",
                    DisplayName = $"Feed {totalCattle} cattle, {hens} hens",
                    Minutes = Math.Max(1, Mathf.CeilToInt((totalCattle * 2 + hens / 10f))),
                    Notes = "Feeding consumes feed stock (FVS-4 feed loop).",
                });
            }
            if (hens > 0)
            {
                demand.Add(new PlannedFarmTask
                {
                    TaskDefinitionId = PoultryChain.CollectEggsTaskId,
                    DisplayName = "Collect eggs",
                    Minutes = PoultryChain.CollectionMinutes(hens),
                });
            }
            if (churnBatches > 0)
            {
                demand.Add(new PlannedFarmTask
                {
                    TaskDefinitionId = DairyChain.ChurnButterTaskId,
                    DisplayName = $"Churn {churnBatches} batch(es)",
                    Minutes = churnBatches * DairyChain.ChurnMinutesPerBatch,
                    Notes = "Requires the dairy-processing skill (TTS-3 extension path).",
                });
            }
            if (deliveryHandling)
            {
                demand.Add(new PlannedFarmTask
                {
                    TaskDefinitionId = "load-delivery",
                    DisplayName = "Load delivery wagon",
                    Minutes = 30,
                    Notes = "TTS-5 freight math refines this per lot size.",
                });
            }
            if (constructionActive)
            {
                demand.Add(new PlannedFarmTask
                {
                    TaskDefinitionId = FarmConstruction.BuildCoopTaskId,
                    DisplayName = "Construction work",
                    Minutes = 240,
                    Notes = "Only after materials are acquired (ordering enforced).",
                });
            }
            if (feedHarvestToday > 0)
            {
                demand.Add(new PlannedFarmTask
                {
                    TaskDefinitionId = FeedLoop.HarvestHayTaskId,
                    DisplayName = "Harvest hay",
                    Minutes = 120,
                });
            }

            // Assign round-robin against remaining daily budgets.
            var remaining = new Dictionary<EntityId, int>();
            foreach (var w in Workers)
            {
                if (w != null && w.PersonId.IsValid) remaining[w.PersonId] = w.DailyMinutesAvailable;
            }

            foreach (var task in demand)
            {
                EntityId best = EntityId.Invalid;
                string bestName = string.Empty;
                foreach (var w in Workers)
                {
                    if (w == null || !w.PersonId.IsValid) continue;
                    int left;
                    if (remaining.TryGetValue(w.PersonId, out left) && left >= task.Minutes)
                    {
                        best = w.PersonId;
                        bestName = w.Name;
                        break;
                    }
                }
                if (best.IsValid)
                {
                    task.AssignedWorkerId = best;
                    task.AssignedWorkerName = bestName;
                    remaining[best] -= task.Minutes;
                    plan.Tasks.Add(task);
                    plan.TotalMinutes += task.Minutes;
                }
                else
                {
                    plan.Unassigned.Add($"{task.DisplayName} ({task.Minutes} min) — no worker with budget; goes to the overload queue (TTS-2).");
                }
            }

            return plan;
        }
    }
}
