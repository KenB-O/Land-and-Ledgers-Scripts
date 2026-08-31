using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Reputation
{
    [Serializable]
    public sealed class BusinessStockoutMemoryState
    {
        public string categoryId = BusinessReputationState.GeneralStockoutCategoryId;
        public int lastMissedDayIndex = -1;
        public int lastMissedWeekKey = -1;
        public int oneOffMissCount;
        public int consecutiveMissCount;
        public int recoveryStreak;
        public int lifetimeMissCount;
        public float lastSeverity01;

        public BusinessStockoutMemoryState Clone()
        {
            return new BusinessStockoutMemoryState
            {
                categoryId = BusinessReputationState.NormalizeStockoutCategoryId(categoryId),
                lastMissedDayIndex = lastMissedDayIndex,
                lastMissedWeekKey = lastMissedWeekKey,
                oneOffMissCount = Mathf.Max(0, oneOffMissCount),
                consecutiveMissCount = Mathf.Max(0, consecutiveMissCount),
                recoveryStreak = Mathf.Max(0, recoveryStreak),
                lifetimeMissCount = Mathf.Max(0, lifetimeMissCount),
                lastSeverity01 = Mathf.Clamp01(lastSeverity01)
            };
        }

        public void Clamp()
        {
            categoryId = BusinessReputationState.NormalizeStockoutCategoryId(categoryId);
            lastMissedDayIndex = lastMissedDayIndex < 0 ? -1 : lastMissedDayIndex;
            lastMissedWeekKey = lastMissedWeekKey < 0 ? -1 : lastMissedWeekKey;
            oneOffMissCount = Mathf.Max(0, oneOffMissCount);
            consecutiveMissCount = Mathf.Max(0, consecutiveMissCount);
            recoveryStreak = Mathf.Max(0, recoveryStreak);
            lifetimeMissCount = Mathf.Max(0, lifetimeMissCount);
            lastSeverity01 = Mathf.Clamp01(lastSeverity01);
        }

        public float Pressure01
        {
            get
            {
                float streakPressure = Mathf.Clamp01(consecutiveMissCount / 4f);
                float oneOffPressure = Mathf.Clamp01(oneOffMissCount / 8f);
                float lifetimePressure = Mathf.Clamp01(lifetimeMissCount / 12f);
                float severityPressure = Mathf.Clamp01(lastSeverity01);
                float recoveryRelief = Mathf.Clamp01(recoveryStreak / 6f) * 0.5f;
                return Mathf.Clamp01(streakPressure * 0.48f + oneOffPressure * 0.12f + lifetimePressure * 0.17f + severityPressure * 0.23f - recoveryRelief);
            }
        }
    }

    [Serializable]
    public sealed class BusinessReputationState
    {
        public const string GeneralStockoutCategoryId = "__general__";

        public float stockReliability01 = 0.5f;
        public float valueFairness01 = 0.5f;
        public float serviceExperience01 = 0.5f;
        public float conditionPresentationTrust01 = 0.5f;
        public float productTradeConfidence01 = 0.5f;
        public bool initializedFromRuntime;
        public List<BusinessStockoutMemoryState> stockoutMemory = new();

        public BusinessReputationState()
        {
        }

        public BusinessReputationState(float stockReliability01, float valueFairness01, float serviceExperience01, float conditionPresentationTrust01, float productTradeConfidence01, bool initializedFromRuntime = true)
        {
            this.stockReliability01 = Mathf.Clamp01(stockReliability01);
            this.valueFairness01 = Mathf.Clamp01(valueFairness01);
            this.serviceExperience01 = Mathf.Clamp01(serviceExperience01);
            this.conditionPresentationTrust01 = Mathf.Clamp01(conditionPresentationTrust01);
            this.productTradeConfidence01 = Mathf.Clamp01(productTradeConfidence01);
            this.initializedFromRuntime = initializedFromRuntime;
        }

        public static string NormalizeStockoutCategoryId(string categoryId)
        {
            return string.IsNullOrWhiteSpace(categoryId) ? GeneralStockoutCategoryId : categoryId.Trim();
        }

        public BusinessReputationState Clone()
        {
            BusinessReputationState clone = new BusinessReputationState(stockReliability01, valueFairness01, serviceExperience01, conditionPresentationTrust01, productTradeConfidence01, initializedFromRuntime);
            clone.stockoutMemory.Clear();
            if (stockoutMemory != null)
            {
                for (int i = 0; i < stockoutMemory.Count; i++)
                {
                    if (stockoutMemory[i] != null)
                    {
                        clone.stockoutMemory.Add(stockoutMemory[i].Clone());
                    }
                }
            }

            clone.Clamp();
            return clone;
        }

        public void Clamp()
        {
            stockReliability01 = Mathf.Clamp01(stockReliability01);
            valueFairness01 = Mathf.Clamp01(valueFairness01);
            serviceExperience01 = Mathf.Clamp01(serviceExperience01);
            conditionPresentationTrust01 = Mathf.Clamp01(conditionPresentationTrust01);
            productTradeConfidence01 = Mathf.Clamp01(productTradeConfidence01);
            stockoutMemory ??= new List<BusinessStockoutMemoryState>();
            for (int i = stockoutMemory.Count - 1; i >= 0; i--)
            {
                BusinessStockoutMemoryState memory = stockoutMemory[i];
                if (memory == null)
                {
                    stockoutMemory.RemoveAt(i);
                    continue;
                }

                memory.Clamp();
            }

            MergeDuplicateStockoutMemory();
        }

        public void ApplyRuntimeBaseline(float stockHealth01, float runtimeReliability01, float operatingEfficiency01)
        {
            float stock = Mathf.Clamp01(stockHealth01);
            float reliability = Mathf.Clamp01(runtimeReliability01);
            float efficiency = Mathf.Clamp01(operatingEfficiency01);
            stockReliability01 = stock;
            valueFairness01 = 0.5f;
            serviceExperience01 = efficiency;
            conditionPresentationTrust01 = reliability;
            productTradeConfidence01 = Mathf.Clamp01(stock * 0.55f + reliability * 0.45f);
            initializedFromRuntime = true;
            Clamp();
        }

        public BusinessStockoutMemoryState FindOrCreateStockoutMemory(string categoryId)
        {
            string key = NormalizeStockoutCategoryId(categoryId);
            stockoutMemory ??= new List<BusinessStockoutMemoryState>();
            for (int i = 0; i < stockoutMemory.Count; i++)
            {
                BusinessStockoutMemoryState memory = stockoutMemory[i];
                if (memory != null && string.Equals(NormalizeStockoutCategoryId(memory.categoryId), key, StringComparison.OrdinalIgnoreCase))
                {
                    memory.categoryId = key;
                    return memory;
                }
            }

            BusinessStockoutMemoryState created = new() { categoryId = key };
            stockoutMemory.Add(created);
            return created;
        }

        public void RecordStockout(string categoryId, int absoluteDayIndex, int weekKey, float severity01)
        {
            BusinessStockoutMemoryState memory = FindOrCreateStockoutMemory(categoryId);
            memory.lastMissedDayIndex = absoluteDayIndex >= 0 ? absoluteDayIndex : memory.lastMissedDayIndex;
            memory.lastMissedWeekKey = weekKey >= 0 ? weekKey : memory.lastMissedWeekKey;
            memory.lifetimeMissCount++;
            memory.oneOffMissCount++;
            memory.consecutiveMissCount++;
            memory.recoveryStreak = 0;
            memory.lastSeverity01 = Mathf.Clamp01(severity01 <= 0f ? 1f : severity01);
            Clamp();
        }

        public void RecordStockoutRecovery(string categoryId, float fillRate01)
        {
            BusinessStockoutMemoryState memory = FindOrCreateStockoutMemory(categoryId);
            if (fillRate01 >= 0.95f)
            {
                memory.recoveryStreak++;
                if (memory.recoveryStreak >= 2)
                {
                    memory.consecutiveMissCount = Mathf.Max(0, memory.consecutiveMissCount - 1);
                    memory.oneOffMissCount = Mathf.Max(0, memory.oneOffMissCount - 1);
                    memory.lastSeverity01 = Mathf.Clamp01(memory.lastSeverity01 - 0.12f);
                }
            }
            else
            {
                memory.recoveryStreak = 0;
            }

            Clamp();
        }

        public float GetStockoutPressure01(string categoryId)
        {
            stockoutMemory ??= new List<BusinessStockoutMemoryState>();
            string key = NormalizeStockoutCategoryId(categoryId);
            if (key != GeneralStockoutCategoryId)
            {
                float categoryPressure = 0f;
                bool foundCategory = false;
                float generalPressure = 0f;
                bool foundGeneral = false;
                for (int i = 0; i < stockoutMemory.Count; i++)
                {
                    BusinessStockoutMemoryState memory = stockoutMemory[i];
                    if (memory == null)
                    {
                        continue;
                    }

                    string memoryKey = NormalizeStockoutCategoryId(memory.categoryId);
                    if (string.Equals(memoryKey, key, StringComparison.OrdinalIgnoreCase))
                    {
                        categoryPressure = Mathf.Max(categoryPressure, memory.Pressure01);
                        foundCategory = true;
                    }
                    else if (string.Equals(memoryKey, GeneralStockoutCategoryId, StringComparison.OrdinalIgnoreCase))
                    {
                        generalPressure = Mathf.Max(generalPressure, memory.Pressure01);
                        foundGeneral = true;
                    }
                }

                if (foundCategory)
                {
                    return Mathf.Clamp01(categoryPressure + (foundGeneral ? generalPressure * 0.35f : 0f));
                }

                return foundGeneral ? Mathf.Clamp01(generalPressure * 0.65f) : 0f;
            }

            if (stockoutMemory.Count == 0)
            {
                return 0f;
            }

            float total = 0f;
            int count = 0;
            for (int i = 0; i < stockoutMemory.Count; i++)
            {
                if (stockoutMemory[i] == null)
                {
                    continue;
                }

                total += stockoutMemory[i].Pressure01;
                count++;
            }

            return count == 0 ? 0f : Mathf.Clamp01(total / count);
        }

        private void MergeDuplicateStockoutMemory()
        {
            if (stockoutMemory == null || stockoutMemory.Count <= 1)
            {
                return;
            }

            Dictionary<string, BusinessStockoutMemoryState> merged = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < stockoutMemory.Count; i++)
            {
                BusinessStockoutMemoryState memory = stockoutMemory[i];
                if (memory == null)
                {
                    continue;
                }

                string key = NormalizeStockoutCategoryId(memory.categoryId);
                memory.categoryId = key;
                if (merged.TryGetValue(key, out BusinessStockoutMemoryState existing))
                {
                    MergeStockoutMemory(existing, memory);
                }
                else
                {
                    merged[key] = memory;
                }
            }

            stockoutMemory.Clear();
            foreach (BusinessStockoutMemoryState memory in merged.Values)
            {
                memory.Clamp();
                stockoutMemory.Add(memory);
            }
        }

        private static void MergeStockoutMemory(BusinessStockoutMemoryState target, BusinessStockoutMemoryState source)
        {
            target.lastMissedDayIndex = Mathf.Max(target.lastMissedDayIndex, source.lastMissedDayIndex);
            target.lastMissedWeekKey = Mathf.Max(target.lastMissedWeekKey, source.lastMissedWeekKey);
            target.oneOffMissCount += Mathf.Max(0, source.oneOffMissCount);
            target.consecutiveMissCount = Mathf.Max(target.consecutiveMissCount, source.consecutiveMissCount);
            target.recoveryStreak = Mathf.Max(target.recoveryStreak, source.recoveryStreak);
            target.lifetimeMissCount += Mathf.Max(0, source.lifetimeMissCount);
            target.lastSeverity01 = Mathf.Max(target.lastSeverity01, source.lastSeverity01);
        }
    }
}
