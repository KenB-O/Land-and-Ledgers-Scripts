using System;
using LandLedgers.Economy.Trade;

namespace LandLedgers.Economy.Blacksmith
{
    /// <summary>
    /// D4 (F-D2-3): the signal type for the smith's iron reorder policy.
    /// DATA only — the signal never orders. The import driver reads it
    /// and decides. A signal with ShouldOrder false is a clean "no
    /// action" and is never an error.
    /// </summary>
    public sealed class BlacksmithIronReorderSignal
    {
        /// <summary>True when covered iron (on hand + outstanding) sits at or below the threshold.</summary>
        public bool ShouldOrder;
        /// <summary>Iron units on hand plus outstanding (Ordered, not Delivered) iron import units.</summary>
        public int CoveredUnits;
        /// <summary>Iron units in the smith's material stock right now.</summary>
        public int IronOnHandUnits;
        /// <summary>Units in outstanding iron import orders (Ordered, not Delivered).</summary>
        public int OutstandingIronUnits;
        /// <summary>The fixed lot the driver orders when the signal fires.</summary>
        public int OrderUnits;
        /// <summary>The registered import material id ("iron-stock").</summary>
        public string MaterialId = string.Empty;
        /// <summary>Human-readable reason, for the driver log.</summary>
        public string Reason = string.Empty;
    }

    /// <summary>
    /// D4 (F-D2-3): the smith's iron reorder policy. Iron is an
    /// imported consumable — it arrives from Pittsburgh ironworks via
    /// the railhead (registered in ImportCatalog: "iron-stock", 220 mi,
    /// 14 days transit, 45c per unit) — and a smith with no iron cannot
    /// forge. The policy keeps the smith stocked by a covered-stock
    /// rule: on-hand iron plus outstanding import units, against a
    /// threshold. One iron unit forges 4 shoes or 4 hardware pieces; a
    /// plow takes 8 units, a harrow 6, a wagon part 4, a hand tool 1. A
    /// working smith burns roughly a unit a day on shoes and small
    /// hardware, so the threshold is 8 units — two plows' worth, or 32
    /// shoes — about half a 14-day transit cycle, a floor that still
    /// lets one plow start while a replenishment is in transit. The
    /// order lot is 16 units (720c), about a transit cycle of steady
    /// forging, a sane bulk lot for a 220-mile import leg. Both numbers
    /// are provisional calibrations for the owner to correct; a shop
    /// may set them per smithy.
    ///
    /// The single-outstanding-order guard (the Millbrook pattern): the
    /// driver adds outstanding units to on-hand units before deciding,
    /// so a covered stock never triggers a second order while the
    /// first is in transit.
    /// </summary>
    public sealed class BlacksmithIronReorderPolicy
    {
        /// <summary>
        /// D4: the covered-stock floor, in iron units. At or below this,
        /// the signal fires. Provisional calibration — the owner
        /// corrects the figure. Settable per shop.
        /// </summary>
        public int IronThresholdUnits { get; set; } = 8;

        /// <summary>
        /// D4: the fixed import lot, in iron units. Provisional
        /// calibration — the owner corrects the figure. Settable per
        /// shop.
        /// </summary>
        public int IronOrderUnits { get; set; } = 16;

        /// <summary>
        /// D4: evaluates the smith's covered iron stock. The signal
        /// never orders — it reports. A null smithy returns a "no action"
        /// signal with the reason named, never an exception.
        /// </summary>
        public BlacksmithIronReorderSignal Evaluate(BlacksmithRuntime smithy, int outstandingIronUnits)
        {
            var signal = new BlacksmithIronReorderSignal
            {
                MaterialId = ImportCatalog.IronStockId,
                OrderUnits = IronOrderUnits,
                OutstandingIronUnits = Math.Max(0, outstandingIronUnits),
            };
            if (smithy == null)
            {
                signal.ShouldOrder = false;
                signal.IronOnHandUnits = 0;
                signal.CoveredUnits = signal.OutstandingIronUnits;
                signal.Reason = "BlacksmithIronReorderPolicy: no smithy to evaluate — no action.";
                return signal;
            }
            signal.IronOnHandUnits = smithy.MaterialOnHand(ImportCatalog.IronStockId);
            signal.CoveredUnits = signal.IronOnHandUnits + signal.OutstandingIronUnits;
            if (signal.CoveredUnits <= IronThresholdUnits)
            {
                signal.ShouldOrder = true;
                signal.Reason = $"BlacksmithIronReorderPolicy: covered iron {signal.CoveredUnits} (on hand {signal.IronOnHandUnits} + outstanding {signal.OutstandingIronUnits}) at or below threshold {IronThresholdUnits} — order {IronOrderUnits} units of {signal.MaterialId}.";
            }
            else
            {
                signal.ShouldOrder = false;
                signal.Reason = $"BlacksmithIronReorderPolicy: covered iron {signal.CoveredUnits} (on hand {signal.IronOnHandUnits} + outstanding {signal.OutstandingIronUnits}) above threshold {IronThresholdUnits} — no order.";
            }
            return signal;
        }
    }
}
