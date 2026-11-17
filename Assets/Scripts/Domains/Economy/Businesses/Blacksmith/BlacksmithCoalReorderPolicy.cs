using System;
using LandLedgers.Economy.Trade;

namespace LandLedgers.Economy.Blacksmith
{
    /// <summary>
    /// D5 (D4 follow-up): the signal type for the smith's forge-coal
    /// reorder policy. DATA only — the signal never orders. The import
    /// driver reads it and decides. A signal with ShouldOrder false is
    /// a clean "no action" and is never an error.
    /// </summary>
    public sealed class BlacksmithCoalReorderSignal
    {
        /// <summary>True when covered coal (on hand + outstanding) sits at or below the threshold.</summary>
        public bool ShouldOrder;
        /// <summary>Coal units on hand plus outstanding (Ordered, not Delivered) coal import units.</summary>
        public int CoveredUnits;
        /// <summary>Coal units in the smith's material stock right now.</summary>
        public int CoalOnHandUnits;
        /// <summary>Units in outstanding coal import orders (Ordered, not Delivered).</summary>
        public int OutstandingCoalUnits;
        /// <summary>The fixed lot the driver orders when the signal fires.</summary>
        public int OrderUnits;
        /// <summary>The registered import material id ("forge-coal").</summary>
        public string MaterialId = string.Empty;
        /// <summary>Human-readable reason, for the driver log.</summary>
        public string Reason = string.Empty;
    }

    /// <summary>
    /// D5 (D4 follow-up): the smith's forge-coal reorder policy. Coal
    /// is an imported consumable — it arrives from the off-map coal
    /// dealer via the railhead (registered in ImportCatalog:
    /// "forge-coal", 180 mi, 12 days transit, 18c per unit) — and a
    /// smith with no coal cannot forge. The policy keeps the smith
    /// stocked by a covered-stock rule: on-hand coal plus outstanding
    /// import units, against a threshold. One coal unit fires four
    /// forgings; a working smith burns roughly a unit a day on shoes
    /// and small hardware, so the threshold is 6 units — about half a
    /// 12-day transit cycle, a floor that still lets the forge stay
    /// lit while a replenishment is in transit. The order lot is 12
    /// units (216c), about a transit cycle of steady forging, a sane
    /// bulk lot for a 180-mile import leg. Both numbers are provisional
    /// calibrations for the owner to correct; a shop may set them per
    /// smithy.
    ///
    /// The single-outstanding-order guard (the Millbrook pattern): the
    /// driver adds outstanding units to on-hand units before deciding,
    /// so a covered stock never triggers a second order while the
    /// first is in transit.
    /// </summary>
    public sealed class BlacksmithCoalReorderPolicy
    {
        /// <summary>
        /// D5: the covered-stock floor, in coal units. At or below this,
        /// the signal fires. Provisional calibration — the owner
        /// corrects the figure. Settable per shop.
        /// </summary>
        public int CoalThresholdUnits { get; set; } = 6;

        /// <summary>
        /// D5: the fixed import lot, in coal units. Provisional
        /// calibration — the owner corrects the figure. Settable per
        /// shop.
        /// </summary>
        public int CoalOrderUnits { get; set; } = 12;

        /// <summary>
        /// D5: evaluates the smith's covered coal stock. The signal
        /// never orders — it reports. A null smithy returns a "no action"
        /// signal with the reason named, never an exception.
        /// </summary>
        public BlacksmithCoalReorderSignal Evaluate(BlacksmithRuntime smithy, int outstandingCoalUnits)
        {
            var signal = new BlacksmithCoalReorderSignal
            {
                MaterialId = ImportCatalog.ForgeCoalId,
                OrderUnits = CoalOrderUnits,
                OutstandingCoalUnits = Math.Max(0, outstandingCoalUnits),
            };
            if (smithy == null)
            {
                signal.ShouldOrder = false;
                signal.CoalOnHandUnits = 0;
                signal.CoveredUnits = signal.OutstandingCoalUnits;
                signal.Reason = "BlacksmithCoalReorderPolicy: no smithy to evaluate — no action.";
                return signal;
            }
            signal.CoalOnHandUnits = smithy.MaterialOnHand(ImportCatalog.ForgeCoalId);
            signal.CoveredUnits = signal.CoalOnHandUnits + signal.OutstandingCoalUnits;
            if (signal.CoveredUnits <= CoalThresholdUnits)
            {
                signal.ShouldOrder = true;
                signal.Reason = $"BlacksmithCoalReorderPolicy: covered coal {signal.CoveredUnits} (on hand {signal.CoalOnHandUnits} + outstanding {signal.OutstandingCoalUnits}) at or below threshold {CoalThresholdUnits} — order {CoalOrderUnits} units of {signal.MaterialId}.";
            }
            else
            {
                signal.ShouldOrder = false;
                signal.Reason = $"BlacksmithCoalReorderPolicy: covered coal {signal.CoveredUnits} (on hand {signal.CoalOnHandUnits} + outstanding {signal.OutstandingCoalUnits}) above threshold {CoalThresholdUnits} — no order.";
            }
            return signal;
        }
    }
}
