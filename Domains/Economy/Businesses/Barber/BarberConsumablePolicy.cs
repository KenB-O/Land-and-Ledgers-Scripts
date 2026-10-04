using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Barber
{
    /// <summary>
    /// D1B: the shop's consumable reorder policy. The bootstrap endowment is
    /// one-time and lots never auto-replenish (upstream-provenance doctrine) —
    /// so the shop needs a visible reorder signal before the shelf runs dry.
    /// This produces DATA: the caller places real import orders through
    /// ImportService (BarberConsumableSupply), which arrive as named lots.
    /// Thresholds are calibration (Canon Part XV), settable per shop.
    /// </summary>
    [Serializable]
    public sealed class BarberConsumableReorderPolicy
    {
        public int SoapThresholdUnits = 12;
        public int SoapOrderUnits = 40;
        public int LinenThresholdUnits = 9;
        public int LinenOrderUnits = 30;

        public BarberConsumableReorderPolicy() { }
    }

    /// <summary>D1B: one reorder signal — a consumable at or below its threshold.</summary>
    [Serializable]
    public sealed class BarberConsumableReorderSignal
    {
        public string ConsumableName = string.Empty;
        public int UnitsOnHand;
        public int ThresholdUnits;
        public int SuggestedOrderUnits;
        public int DayIndex;

        /// <summary>
        /// Days a real import order takes to arrive (BarberConsumableSupply
        /// registers 14 transit days — named origin, real distance, real cost).
        /// </summary>
        public int LeadTimeDays;

        public string Reason = string.Empty;

        public BarberConsumableReorderSignal() { }
    }

    /// <summary>D1B: evaluates reorder signals against the consumable shelf.</summary>
    public static class BarberConsumablePolicy
    {
        /// <summary>TUNING: transit days for a real soap/linen import order (BarberConsumableSupply: 14 days via railhead).</summary>
        public const int ImportLeadTimeDays = 14;

        /// <summary>
        /// Returns a signal for each tracked consumable at or below its
        /// threshold. Empty list = the shelf is fine. Signals never order —
        /// ordering is the caller's real import order; the shelf is never
        /// silently replenished.
        /// </summary>
        public static List<BarberConsumableReorderSignal> EvaluateReorderSignals(
            BarberConsumableStock stock,
            BarberConsumableReorderPolicy policy,
            int dayIndex,
            List<string> diagnostics)
        {
            var signals = new List<BarberConsumableReorderSignal>();
            diagnostics = diagnostics ?? new List<string>();
            policy = policy ?? new BarberConsumableReorderPolicy();
            if (stock == null)
            {
                diagnostics.Add("BarberConsumablePolicy: no consumable stock — reorder not evaluated.");
                return signals;
            }

            CheckConsumable(stock, BarberServiceCatalog.SoapItemId,
                policy.SoapThresholdUnits, policy.SoapOrderUnits, dayIndex, signals, diagnostics);
            CheckConsumable(stock, BarberServiceCatalog.LinenItemId,
                policy.LinenThresholdUnits, policy.LinenOrderUnits, dayIndex, signals, diagnostics);

            if (signals.Count == 0)
            {
                diagnostics.Add("BarberConsumablePolicy: shelf above reorder thresholds — no signal.");
            }

            return signals;
        }

        private static void CheckConsumable(
            BarberConsumableStock stock,
            string consumableName,
            int threshold,
            int orderQty,
            int dayIndex,
            List<BarberConsumableReorderSignal> signals,
            List<string> diagnostics)
        {
            int onHand = stock.UnitsOnHand(consumableName);
            if (onHand > Math.Max(0, threshold)) return;

            var signal = new BarberConsumableReorderSignal
            {
                ConsumableName = consumableName,
                UnitsOnHand = onHand,
                ThresholdUnits = Math.Max(0, threshold),
                SuggestedOrderUnits = Math.Max(1, orderQty),
                DayIndex = dayIndex,
                LeadTimeDays = ImportLeadTimeDays,
                Reason = $"'{consumableName}': {onHand} units on hand at/below threshold {threshold} — " +
                    "place a real import order (BarberConsumableSupply); lots never auto-replenish.",
            };
            signals.Add(signal);
            diagnostics.Add($"BarberConsumablePolicy: REORDER SIGNAL — {signal.Reason}");
        }
    }
}
