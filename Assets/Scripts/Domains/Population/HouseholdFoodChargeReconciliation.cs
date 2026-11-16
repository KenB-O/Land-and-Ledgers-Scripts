using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// DEF-067: which charge path a household food purchase came through.
    /// Legacy is the HF-4 embodied-purchase executor (DailyNeedsService
    /// reserve path + planner); PhaseC is the Phase C shopping loop
    /// (shortage monitor + HouseholdShoppingLoop settlement).
    /// </summary>
    public enum HouseholdFoodChargePath
    {
        Legacy = 0,
        PhaseC = 1,
    }

    /// <summary>
    /// DEF-067: one structured food-charge record. A charge is a real
    /// household debit: cash outflow through the household ledger, or a
    /// trade-credit obligation the household owes. Voided and failed
    /// settlements record nothing — only a posted charge is recorded.
    /// WantKey is the key the charging path used: a category id for the
    /// legacy path, an item id for the Phase C path. The want-category
    /// (see HouseholdFoodChargeReconciliation.WantCategoryForKey) is what
    /// makes the two keys comparable.
    /// </summary>
    [Serializable]
    public sealed class HouseholdFoodChargeRecord
    {
        public int HouseholdId;
        public int DayIndex;
        public HouseholdFoodChargePath Path;
        public string WantKey = string.Empty;
        public int Units;
        public int GoodsCents;
        public int FreightCents;
        /// <summary>Household debit: cash outflow, or total owed in credit mode.</summary>
        public int AmountCents;
        public int NeedSequence = -1;
        public string Counterparty = string.Empty;
        public string SettlementMode = string.Empty;
    }

    /// <summary>
    /// DEF-067: per-want-category totals for one household-day.
    /// </summary>
    [Serializable]
    public sealed class WantChargeTotals
    {
        public string WantCategory = string.Empty;
        public int LegacyUnits;
        public int LegacyCents;
        public int PhaseCUnits;
        public int PhaseCCents;
        public List<string> PhaseCItems = new List<string>();
    }

    /// <summary>
    /// DEF-067: one household-day's food-charge summary. The two flags are
    /// the evidence the next Unity run needs: BothPathsChargedSameWant
    /// fires when the legacy path and the Phase C path both charged the
    /// same want-category on the same day; PhaseCChargedMultipleItemsSameWant
    /// fires when Phase C settled more than one item in the same
    /// want-category on the same day (the monitor sizes each item's need
    /// for the full daily requirement while meals consume one item per
    /// serving — see plan-def-067-evidence.md, mechanism M1).
    /// </summary>
    [Serializable]
    public sealed class DayFoodChargeSummary
    {
        public int HouseholdId;
        public int DayIndex;
        public List<WantChargeTotals> PerWant = new List<WantChargeTotals>();
        public bool BothPathsChargedSameWant;
        public List<string> OverlappedWants = new List<string>();
        public bool PhaseCChargedMultipleItemsSameWant;
        public List<string> MultiItemWants = new List<string>();
        public int LegacyUnits;
        public int LegacyCents;
        public int PhaseCUnits;
        public int PhaseCCents;
    }

    /// <summary>
    /// DEF-067: diagnostics-only reconciliation of household food charges.
    /// Append-only, in-memory; nothing here is saved or loaded, and
    /// recording never throws and never fails a purchase (a null recorder
    /// at the charge points means today's behavior exactly). The daily
    /// summary line is what the Unity console captures for the
    /// legacy-vs-Phase-C day-by-day comparison.
    /// </summary>
    public sealed class HouseholdFoodChargeReconciliation
    {
        private readonly List<HouseholdFoodChargeRecord> records = new List<HouseholdFoodChargeRecord>();

        public IReadOnlyList<HouseholdFoodChargeRecord> Records => records;

        /// <summary>
        /// Maps a charge key to its want-category. An item id maps through
        /// the catalog's own ReserveCategoryId (the authoritative
        /// category-to-item mapping); a legacy category key is already a
        /// want-category and passes through unchanged. Unknown keys map to
        /// themselves — no mapping is invented.
        /// </summary>
        public static string WantCategoryForKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            HouseholdItemDefinition definition = HouseholdItemCatalog.Get(key);
            if (definition != null && !string.IsNullOrWhiteSpace(definition.ReserveCategoryId))
            {
                return definition.ReserveCategoryId;
            }

            return key;
        }

        public void RecordLegacy(int householdId, int dayIndex, string categoryId, int units, int amountCents, int needSequence, string counterparty)
        {
            if (householdId < 0 || units <= 0 || string.IsNullOrWhiteSpace(categoryId))
            {
                return;
            }

            records.Add(new HouseholdFoodChargeRecord
            {
                HouseholdId = householdId,
                DayIndex = Math.Max(0, dayIndex),
                Path = HouseholdFoodChargePath.Legacy,
                WantKey = categoryId,
                Units = units,
                GoodsCents = Math.Max(0, amountCents),
                FreightCents = 0,
                AmountCents = Math.Max(0, amountCents),
                NeedSequence = needSequence,
                Counterparty = counterparty ?? string.Empty,
                SettlementMode = "cash",
            });
        }

        public void RecordPhaseC(int householdId, int dayIndex, string itemId, int unitsSold, int goodsCents, int freightCents, int amountCents, int needSequence, string counterparty, string mode)
        {
            if (householdId < 0 || unitsSold <= 0 || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            records.Add(new HouseholdFoodChargeRecord
            {
                HouseholdId = householdId,
                DayIndex = Math.Max(0, dayIndex),
                Path = HouseholdFoodChargePath.PhaseC,
                WantKey = itemId,
                Units = unitsSold,
                GoodsCents = Math.Max(0, goodsCents),
                FreightCents = Math.Max(0, freightCents),
                AmountCents = Math.Max(0, amountCents),
                NeedSequence = needSequence,
                Counterparty = counterparty ?? string.Empty,
                SettlementMode = mode ?? string.Empty,
            });
        }

        public List<HouseholdFoodChargeRecord> GetRecords(int householdId, int dayIndex)
        {
            var result = new List<HouseholdFoodChargeRecord>();
            foreach (HouseholdFoodChargeRecord record in records)
            {
                if (record != null && record.HouseholdId == householdId && record.DayIndex == dayIndex)
                {
                    result.Add(record);
                }
            }

            return result;
        }

        public DayFoodChargeSummary SummarizeDay(int householdId, int dayIndex)
        {
            var summary = new DayFoodChargeSummary
            {
                HouseholdId = householdId,
                DayIndex = Math.Max(0, dayIndex),
            };
            var byWant = new Dictionary<string, WantChargeTotals>(StringComparer.Ordinal);
            foreach (HouseholdFoodChargeRecord record in GetRecords(householdId, dayIndex))
            {
                string want = WantCategoryForKey(record.WantKey);
                if (!byWant.TryGetValue(want, out WantChargeTotals totals))
                {
                    totals = new WantChargeTotals { WantCategory = want };
                    byWant[want] = totals;
                    summary.PerWant.Add(totals);
                }

                if (record.Path == HouseholdFoodChargePath.Legacy)
                {
                    totals.LegacyUnits += record.Units;
                    totals.LegacyCents += record.AmountCents;
                    summary.LegacyUnits += record.Units;
                    summary.LegacyCents += record.AmountCents;
                }
                else
                {
                    totals.PhaseCUnits += record.Units;
                    totals.PhaseCCents += record.AmountCents;
                    summary.PhaseCUnits += record.Units;
                    summary.PhaseCCents += record.AmountCents;
                    if (!totals.PhaseCItems.Contains(record.WantKey))
                    {
                        totals.PhaseCItems.Add(record.WantKey);
                    }
                }
            }

            foreach (WantChargeTotals totals in summary.PerWant)
            {
                if (totals.LegacyUnits > 0 && totals.PhaseCUnits > 0)
                {
                    summary.BothPathsChargedSameWant = true;
                    summary.OverlappedWants.Add(totals.WantCategory);
                }

                if (totals.PhaseCItems.Count > 1)
                {
                    summary.PhaseCChargedMultipleItemsSameWant = true;
                    summary.MultiItemWants.Add(totals.WantCategory);
                }
            }

            return summary;
        }

        /// <summary>
        /// One compact line for the whole day across households, for the
        /// Unity console. Emitted every day, even all-zero — a missing
        /// line must never look like a logging cutoff.
        /// </summary>
        public string SummarizeDayLine(int dayIndex)
        {
            dayIndex = Math.Max(0, dayIndex);
            var householdIds = new List<int>();
            foreach (HouseholdFoodChargeRecord record in records)
            {
                if (record != null && record.DayIndex == dayIndex && !householdIds.Contains(record.HouseholdId))
                {
                    householdIds.Add(record.HouseholdId);
                }
            }

            if (householdIds.Count == 0)
            {
                return $"[FoodEconomy] charge reconciliation day {dayIndex}: no food charges recorded.";
            }

            var parts = new List<string>();
            var flags = new List<string>();
            foreach (int householdId in householdIds)
            {
                DayFoodChargeSummary summary = SummarizeDay(householdId, dayIndex);
                parts.Add($"H{householdId} legacy={summary.LegacyUnits}u/{summary.LegacyCents}c phaseC={summary.PhaseCUnits}u/{summary.PhaseCCents}c");
                foreach (string want in summary.OverlappedWants)
                {
                    flags.Add($"OVERLAP {want} (H{householdId})");
                }

                foreach (string want in summary.MultiItemWants)
                {
                    WantChargeTotals totals = null;
                    foreach (WantChargeTotals candidate in summary.PerWant)
                    {
                        if (string.Equals(candidate.WantCategory, want, StringComparison.Ordinal))
                        {
                            totals = candidate;
                            break;
                        }
                    }

                    string items = totals != null ? string.Join(",", totals.PhaseCItems) : string.Empty;
                    flags.Add($"MULTI-ITEM {want} ({items}) (H{householdId})");
                }
            }

            string flagText = flags.Count > 0 ? "; flags: " + string.Join("; ", flags) : string.Empty;
            return $"[FoodEconomy] charge reconciliation day {dayIndex}: " + string.Join("; ", parts) + flagText;
        }
    }

    /// <summary>
    /// Emits a day's food-charge reconciliation only after that simulated
    /// day has completed. Physical shopping settles during the day, after
    /// the daily needs drive starts, so logging the current day's summary
    /// from that drive produces a misleading early snapshot.
    /// </summary>
    public sealed class HouseholdFoodChargeReconciliationReporter
    {
        private int lastPublishedDayIndex = -1;

        public bool TryCreateCompletedDayLine(
            HouseholdFoodChargeReconciliation reconciliation,
            int completedDayIndex,
            out string line)
        {
            line = null;
            if (reconciliation == null || completedDayIndex < 0)
            {
                return false;
            }

            if (completedDayIndex == lastPublishedDayIndex)
            {
                return false;
            }

            line = reconciliation.SummarizeDayLine(completedDayIndex);
            lastPublishedDayIndex = completedDayIndex;
            return true;
        }
    }
}
