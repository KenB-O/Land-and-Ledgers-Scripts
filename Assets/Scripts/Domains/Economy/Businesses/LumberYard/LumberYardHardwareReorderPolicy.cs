using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// D3 (F-D2-2): the signal type for the yard's nails reorder policy.
    /// DATA only — the signal never orders. The import driver reads it
    /// and decides. A signal with ShouldOrder false is a clean "no
    /// action" and is never an error.
    /// </summary>
    public sealed class LumberYardHardwareReorderSignal
    {
        /// <summary>True when covered nails (on hand + outstanding) sit at or below the threshold.</summary>
        public bool ShouldOrder;
        /// <summary>Nails on hand (all nails lots, any source) plus outstanding (Ordered, not Delivered) nails import units.</summary>
        public int CoveredUnits;
        /// <summary>Nails units in yard hardware stock right now.</summary>
        public int NailsOnHandUnits;
        /// <summary>Units in outstanding nails import orders (Ordered, not Delivered).</summary>
        public int OutstandingNailsUnits;
        /// <summary>The fixed lot the driver orders when the signal fires.</summary>
        public int OrderUnits;
        /// <summary>The registered import material id ("nails-hardware").</summary>
        public string MaterialId = string.Empty;
        /// <summary>Human-readable reason, for the driver log.</summary>
        public string Reason = string.Empty;
    }

    /// <summary>
    /// D3 (F-D2-2): the yard's nails reorder policy. Nails are an
    /// imported consumable — they arrive from Pittsburgh ironworks via
    /// the railhead (registered by
    /// LumberYardSupply.EnsureHardwareImportable, 30c per unit, 14 days
    /// transit) — and a construction schedule that runs out of nails
    /// stops. The policy keeps the yard stocked by a covered-stock
    /// rule: on-hand nails plus outstanding import units, against a
    /// threshold. The threshold is the day's construction demand
    /// against the 14-day transit: a settler cabin needs about 3 nails
    /// units, a family cabin or a workshop about 6, so 12 units — two
    /// family cabins' worth — is the floor that still lets one cabin
    /// start while a replenishment is in transit. The order lot is 60
    /// units, about ten cabins' worth, a sane bulk lot for a 220-mile
    /// import leg. Both numbers are provisional calibrations for the
    /// owner to correct; a shop may set them per yard.
    ///
    /// The single-outstanding-order guard (the Millbrook pattern): the
    /// driver adds outstanding units to on-hand units before deciding,
    /// so a covered stock never triggers a second order while the
    /// first is in transit.
    /// </summary>
    public sealed class LumberYardHardwareReorderPolicy
    {
        /// <summary>
        /// D3: the covered-stock floor, in nails units. At or below this,
        /// the signal fires. Provisional calibration — the owner
        /// corrects the figure. Settable per shop.
        /// </summary>
        public int NailsThresholdUnits { get; set; } = 12;

        /// <summary>
        /// D3: the fixed import lot, in nails units. Provisional
        /// calibration — the owner corrects the figure. Settable per
        /// shop.
        /// </summary>
        public int NailsOrderUnits { get; set; } = 60;

        /// <summary>
        /// D3: evaluates the yard's covered nails stock. The signal
        /// never orders — it reports. A null yard returns a "no action"
        /// signal with the reason named, never an exception.
        /// </summary>
        public LumberYardHardwareReorderSignal Evaluate(LumberYardShopRuntime yard, int outstandingNailsUnits)
        {
            var signal = new LumberYardHardwareReorderSignal
            {
                MaterialId = LumberYardSupply.HardwareImportMaterialId,
                OrderUnits = NailsOrderUnits,
                OutstandingNailsUnits = Math.Max(0, outstandingNailsUnits),
            };
            if (yard == null || yard.HardwareStock == null)
            {
                signal.ShouldOrder = false;
                signal.NailsOnHandUnits = 0;
                signal.CoveredUnits = signal.OutstandingNailsUnits;
                signal.Reason = "LumberYardHardwareReorderPolicy: no yard hardware stock to evaluate — no action.";
                return signal;
            }
            signal.NailsOnHandUnits = yard.HardwareStock.AvailableUnits(LumberYardHardwareKind.Nails);
            signal.CoveredUnits = signal.NailsOnHandUnits + signal.OutstandingNailsUnits;
            if (signal.CoveredUnits <= NailsThresholdUnits)
            {
                signal.ShouldOrder = true;
                signal.Reason = $"LumberYardHardwareReorderPolicy: covered nails {signal.CoveredUnits} (on hand {signal.NailsOnHandUnits} + outstanding {signal.OutstandingNailsUnits}) at or below threshold {NailsThresholdUnits} — order {NailsOrderUnits} units of {signal.MaterialId}.";
            }
            else
            {
                signal.ShouldOrder = false;
                signal.Reason = $"LumberYardHardwareReorderPolicy: covered nails {signal.CoveredUnits} (on hand {signal.NailsOnHandUnits} + outstanding {signal.OutstandingNailsUnits}) above threshold {NailsThresholdUnits} — no order.";
            }
            return signal;
        }
    }
}
