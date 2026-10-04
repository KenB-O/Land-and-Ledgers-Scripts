using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Tailor
{
    /// <summary>
    /// D1C: the cloth shelf's reorder policy. The bootstrap endowment is
    /// one-time and lots never auto-replenish (upstream-provenance doctrine)
    /// — so the shop needs a visible reorder signal before the shelf runs
    /// dry. This produces DATA: the caller places real import orders through
    /// ImportService (TailorClothSupply), which arrive as named lots.
    /// Thresholds are calibration (Canon Part XV), settable per shop.
    /// Mirrors the D1B barber consumable policy pattern.
    /// </summary>
    [Serializable]
    public sealed class TailorClothReorderPolicy
    {
        /// <summary>TUNING: cloth yards at/below which a reorder signal fires.</summary>
        public int ClothThresholdYards = 9;

        /// <summary>TUNING: suggested cloth yards on the reorder.</summary>
        public int ClothOrderYards = 30;

        /// <summary>TUNING: notion units at/below which a reorder signal fires.</summary>
        public int NotionsThresholdUnits = 6;

        /// <summary>TUNING: suggested notion units on the reorder.</summary>
        public int NotionsOrderUnits = 20;

        public TailorClothReorderPolicy() { }
    }

    /// <summary>D1C: one reorder signal — cloth or notions at or below threshold.</summary>
    [Serializable]
    public sealed class TailorClothReorderSignal
    {
        public string MaterialName = string.Empty;
        public int UnitsOnHand;
        public int ThresholdUnits;
        public int SuggestedOrderUnits;
        public int DayIndex;

        /// <summary>
        /// Days a real import order takes to arrive (TailorClothSupply
        /// registers 14 transit days — named origin, real distance, real cost).
        /// </summary>
        public int LeadTimeDays;

        public string Reason = string.Empty;

        public TailorClothReorderSignal() { }
    }

    /// <summary>D1C: evaluates reorder signals against the cloth shelf.</summary>
    public static class TailorClothReorderEvaluator
    {
        /// <summary>TUNING: transit days for a real cloth/notions import order (TailorClothSupply: 14 days via railhead).</summary>
        public const int ImportLeadTimeDays = 14;

        /// <summary>
        /// Returns a signal for cloth and/or notions at or below threshold.
        /// Empty list = the shelf is fine. Signals never order — ordering is
        /// the caller's real import order; the shelf is never silently
        /// replenished.
        /// </summary>
        public static List<TailorClothReorderSignal> EvaluateReorderSignals(
            TailorClothStock stock,
            TailorClothReorderPolicy policy,
            int dayIndex,
            List<string> diagnostics)
        {
            var signals = new List<TailorClothReorderSignal>();
            diagnostics = diagnostics ?? new List<string>();
            policy = policy ?? new TailorClothReorderPolicy();
            if (stock == null)
            {
                diagnostics.Add("TailorClothReorderEvaluator: no cloth stock — reorder not evaluated.");
                return signals;
            }

            CheckMaterial(stock, TailorGarmentCatalog.ClothItemId,
                policy.ClothThresholdYards, policy.ClothOrderYards, dayIndex, signals, diagnostics);
            CheckMaterial(stock, TailorGarmentCatalog.NotionsItemId,
                policy.NotionsThresholdUnits, policy.NotionsOrderUnits, dayIndex, signals, diagnostics);

            if (signals.Count == 0)
            {
                diagnostics.Add("TailorClothReorderEvaluator: shelf above reorder thresholds — no signal.");
            }

            return signals;
        }

        private static void CheckMaterial(
            TailorClothStock stock,
            string materialName,
            int threshold,
            int orderQty,
            int dayIndex,
            List<TailorClothReorderSignal> signals,
            List<string> diagnostics)
        {
            int onHand = stock.UnitsOnHand(materialName);
            if (onHand > Math.Max(0, threshold)) return;

            var signal = new TailorClothReorderSignal
            {
                MaterialName = materialName,
                UnitsOnHand = onHand,
                ThresholdUnits = Math.Max(0, threshold),
                SuggestedOrderUnits = Math.Max(1, orderQty),
                DayIndex = dayIndex,
                LeadTimeDays = ImportLeadTimeDays,
                Reason = $"'{materialName}': {onHand} units on hand at/below threshold {threshold} — " +
                    "place a real import order (TailorClothSupply); lots never auto-replenish.",
            };
            signals.Add(signal);
            diagnostics.Add($"TailorClothReorderEvaluator: REORDER SIGNAL — {signal.Reason}");
        }
    }
}
