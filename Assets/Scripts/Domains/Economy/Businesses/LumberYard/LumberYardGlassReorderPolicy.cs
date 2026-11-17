using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// F1: the signal type for the yard's window-glass reorder policy.
    /// DATA only — the signal never orders. The import driver reads it
    /// and decides. A signal with ShouldOrder false is a clean "no
    /// action" and is never an error.
    /// </summary>
    public sealed class LumberYardGlassReorderSignal
    {
        /// <summary>True when covered panes (on hand + outstanding) sit at or below the threshold.</summary>
        public bool ShouldOrder;
        /// <summary>Panes on hand (all glass lots, any source) plus outstanding (Ordered, not Delivered) glass import panes.</summary>
        public int CoveredUnits;
        /// <summary>Glass panes in yard glass stock right now.</summary>
        public int GlassOnHandUnits;
        /// <summary>Panes in outstanding glass import orders (Ordered, not Delivered).</summary>
        public int OutstandingGlassUnits;
        /// <summary>The fixed lot the driver orders when the signal fires.</summary>
        public int OrderUnits;
        /// <summary>The registered import material id ("glass-panes").</summary>
        public string MaterialId = string.Empty;
        /// <summary>Human-readable reason, for the driver log.</summary>
        public string Reason = string.Empty;
    }

    /// <summary>
    /// F1: the yard's window-glass reorder policy. Glass panes are an
    /// imported manufactured consumable — they arrive from Pittsburgh
    /// glassworks via the railhead (registered by
    /// LumberYardSupply.EnsureGlassImportable, 25c per pane, 14 days
    /// transit) — and a glazed construction schedule that runs out of
    /// panes stops. The policy keeps the yard stocked by a covered-stock
    /// rule: on-hand panes plus outstanding import panes, against a
    /// threshold. The threshold is the day's construction demand against
    /// the 14-day transit: a settler cabin needs 1 pane, a family cabin
    /// or a workshop 2, so 4 panes — two family cabins' worth — is the
    /// floor that still lets one cabin start while a replenishment is in
    /// transit. The order lot is 12 panes, about six cabins' worth, a
    /// sane bulk lot for a fragile 220-mile import leg (a box of lights).
    /// Both numbers are provisional calibrations for the owner to
    /// correct; a shop may set them per yard.
    ///
    /// The single-outstanding-order guard (the Millbrook pattern): the
    /// driver adds outstanding panes to on-hand panes before deciding,
    /// so a covered stock never triggers a second order while the first
    /// is in transit.
    /// </summary>
    public sealed class LumberYardGlassReorderPolicy
    {
        /// <summary>
        /// F1: the covered-stock floor, in glass panes. At or below this,
        /// the signal fires. Provisional calibration — the owner
        /// corrects the figure. Settable per shop.
        /// </summary>
        public int GlassThresholdUnits { get; set; } = 4;

        /// <summary>
        /// F1: the fixed import lot, in glass panes. Provisional
        /// calibration — the owner corrects the figure. Settable per
        /// shop.
        /// </summary>
        public int GlassOrderUnits { get; set; } = 12;

        /// <summary>
        /// F1: evaluates the yard's covered glass stock. The signal
        /// never orders — it reports. A null yard returns a "no action"
        /// signal with the reason named, never an exception.
        /// </summary>
        public LumberYardGlassReorderSignal Evaluate(LumberYardShopRuntime yard, int outstandingGlassUnits)
        {
            var signal = new LumberYardGlassReorderSignal
            {
                MaterialId = LumberYardSupply.GlassImportMaterialId,
                OrderUnits = GlassOrderUnits,
                OutstandingGlassUnits = Math.Max(0, outstandingGlassUnits),
            };
            if (yard == null || yard.GlassStock == null)
            {
                signal.ShouldOrder = false;
                signal.GlassOnHandUnits = 0;
                signal.CoveredUnits = signal.OutstandingGlassUnits;
                signal.Reason = "LumberYardGlassReorderPolicy: no yard glass stock to evaluate — no action.";
                return signal;
            }
            signal.GlassOnHandUnits = yard.GlassStock.AvailableUnits();
            signal.CoveredUnits = signal.GlassOnHandUnits + signal.OutstandingGlassUnits;
            if (signal.CoveredUnits <= GlassThresholdUnits)
            {
                signal.ShouldOrder = true;
                signal.Reason = $"LumberYardGlassReorderPolicy: covered glass {signal.CoveredUnits} (on hand {signal.GlassOnHandUnits} + outstanding {signal.OutstandingGlassUnits}) at or below threshold {GlassThresholdUnits} — order {GlassOrderUnits} panes of {signal.MaterialId}.";
            }
            else
            {
                signal.ShouldOrder = false;
                signal.Reason = $"LumberYardGlassReorderPolicy: covered glass {signal.CoveredUnits} (on hand {signal.GlassOnHandUnits} + outstanding {signal.OutstandingGlassUnits}) above threshold {GlassThresholdUnits} — no order.";
            }
            return signal;
        }
    }
}
