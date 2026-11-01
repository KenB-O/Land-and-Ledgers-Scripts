// Pass 3 (economy ordering): Fuel Dealer off-map input purchase.
// Kennedy's proposal, queued after Pass 1: give the Fuel Dealer an
// option to buy its raw input off-map, in the same pattern as flour
// for the baker. The raw input is called INPUT GENERICALLY here on
// purpose: what the input actually is (logs, timber, slabs,
// cordwood, or something else) is still OPEN, and this file must not
// hardcode it. A caller supplies the input SKU id; this file never
// invents one, never defaults to "firewood", and never treats a
// guess as a product definition.
//
// What this file does and does not do:
//   - It validates an off-map input order through the same draft
//     rules as every other order (supplier, minimums, pickup-only,
//     freight, automatic-player guard) by wrapping Pass3OrderDraft.
//   - It models the conversion from received input units to fuel
//     output units at a caller-supplied conversion rate. That rate
//     is a test/author parameter, never canon, and conversion is
//     refused unless the input order is actually RECEIVED: an
//     unreceived order converts nothing, so no fuel is conjured
//     from an order that has not arrived.
//   - It does NOT create a real fuel lot in any stock system. The
//     output record carries provenance (supplier, order, input lot)
//     for a driver to feed through the Pass 2 fuel dispense / feed
//     APIs (FuelYardByproductIntake.TryDispenseUnits and the bakery
//     fuel stock) and through BusinessRuntimeState cash. Until a
//     driver does that, the output record is a plan, not stock.
//   - It does NOT spawn free assets because a capability is
//     selected. Equipment, horses, and wagons are bought through
//     the Pass 1 equipment purchase / procurement authorities, with
//     real vendors, prices, payments, deliveries, and asset ids.
//   - Horse and wagon remain DISTINCT owned assets even when
//     operated together. A transport pair is two asset ids, never
//     one merged id, and a pair with a missing id, or with the same
//     id in both fields, is refused outright.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Ordering
{
    /// <summary>
    /// A Fuel Dealer off-map input order: one generic input SKU, from
    /// one supplier, at that supplier's current offer price, with a
    /// caller-supplied conversion rate to fuel output units.
    /// </summary>
    public sealed class Pass3FuelInputOrder
    {
        public string DealerBusinessId = string.Empty;
        public string InputSkuId = string.Empty;
        public Pass3OrderDraft Draft;
        public double ConversionRateFuelPerInputUnit;
        public bool InputReceived;
        public string InputLotId = string.Empty;

        /// <summary>Build an input draft for a generic input SKU. The SKU id is caller data; nothing here defaults it.</summary>
        public static Pass3FuelInputOrder Create(
            string dealerBusinessId, Pass3SupplierOffer supplier, string inputSkuId, int inputQuantityUnits,
            Pass3FreightOption freightOption, int freightCentsPerMile, int freightBusinessCommissionCents,
            double conversionRateFuelPerInputUnit, List<string> diag)
        {
            if (string.IsNullOrWhiteSpace(inputSkuId))
            {
                if (diag != null) diag.Add("Pass3Ordering: fuel input order needs an input SKU id; the raw input type is still open and must be supplied by the caller, never defaulted.");
                return null;
            }
            var draft = new Pass3OrderDraft
            {
                Supplier = supplier,
                FreightOption = freightOption,
                FreightCentsPerMile = freightCentsPerMile,
                FreightBusinessCommissionCents = freightBusinessCommissionCents,
                Actor = Pass3ActorKind.Player,
            };
            if (!draft.AddLine(inputSkuId, inputQuantityUnits, diag)) return null;
            return new Pass3FuelInputOrder
            {
                DealerBusinessId = dealerBusinessId ?? string.Empty,
                InputSkuId = inputSkuId.Trim(),
                Draft = draft,
                ConversionRateFuelPerInputUnit = conversionRateFuelPerInputUnit,
            };
        }

        /// <summary>Place the input draft through the normal order validation (minimums, pickup-only, freight, automatic guard).</summary>
        public List<Pass3OrderRequest> Place(List<string> diag)
        {
            if (Draft == null)
            {
                if (diag != null) diag.Add("Pass3Ordering: fuel input order has no draft to place.");
                return null;
            }
            return Draft.Place(diag);
        }
    }

    /// <summary>
    /// A modelled fuel output from received off-map input: provenance
    /// only, NOT stock. A driver must still move these units into a
    /// real fuel stock through the Pass 2 APIs before they can be
    /// sold or burned.
    /// </summary>
    public sealed class Pass3FuelOutputLot
    {
        public string InputSkuId = string.Empty;
        public string InputLotId = string.Empty;
        public string SupplierId = string.Empty;
        public int InputUnits;
        public int FuelUnits;
        public string Provenance = string.Empty;
    }

    public static class Pass3FuelInputConversion
    {
        /// <summary>
        /// Convert received input into modelled fuel output. Refuses
        /// unreceived input, non-positive input, and non-positive
        /// conversion rates, producing nothing. Fuel units are the
        /// floor of input x rate, so fractional input never conjures
        /// a whole fuel unit.
        /// </summary>
        public static Pass3FuelOutputLot Convert(Pass3FuelInputOrder order, List<string> diag)
        {
            if (order == null || order.Draft == null || order.Draft.Lines == null || order.Draft.Lines.Count == 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: fuel input conversion needs an order with an input line.");
                return null;
            }
            if (!order.InputReceived)
            {
                if (diag != null) diag.Add("Pass3Ordering: fuel input conversion refused: input order has not been received yet.");
                return null;
            }
            if (order.ConversionRateFuelPerInputUnit <= 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: fuel input conversion needs a positive conversion rate.");
                return null;
            }
            int inputUnits = order.Draft.Lines[0].QuantityUnits;
            int fuelUnits = (int)Math.Floor(inputUnits * order.ConversionRateFuelPerInputUnit);
            return new Pass3FuelOutputLot
            {
                InputSkuId = order.InputSkuId,
                InputLotId = order.InputLotId ?? string.Empty,
                SupplierId = order.Draft.Supplier != null ? order.Draft.Supplier.SupplierId : string.Empty,
                InputUnits = inputUnits,
                FuelUnits = fuelUnits,
                Provenance = "off-map input '" + order.InputSkuId + "' via supplier '" + (order.Draft.Supplier != null ? order.Draft.Supplier.SupplierId : string.Empty) + "', input lot '" + (order.InputLotId ?? string.Empty) + "'",
            };
        }
    }

    /// <summary>
    /// A horse-and-wagon transport pair: two distinct owned asset
    /// ids. The pair id is a display string only ("horse+wagon"),
    /// never a merged asset id, and never a substitute for owning
    /// both assets separately.
    /// </summary>
    public sealed class Pass3TransportPair
    {
        public string HorseAssetId = string.Empty;
        public string WagonAssetId = string.Empty;
        public string OwnerBusinessId = string.Empty;

        public string PairDisplayId()
        {
            return HorseAssetId + "+" + WagonAssetId;
        }

        /// <summary>Returns null when the pair is valid, or a refusal reason when it is not.</summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(HorseAssetId)) return "Pass3Ordering: transport pair needs a horse asset id; a wagon alone is not a team.";
            if (string.IsNullOrWhiteSpace(WagonAssetId)) return "Pass3Ordering: transport pair needs a wagon asset id; a horse alone is not a wagon team.";
            if (string.Equals(HorseAssetId.Trim(), WagonAssetId.Trim(), StringComparison.OrdinalIgnoreCase))
                return "Pass3Ordering: horse and wagon must be distinct owned assets; the same id cannot be both.";
            return null;
        }
    }
}
