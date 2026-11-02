// Series 2 Pass 8 (freight + fuel chain): the ISeries2BakeryFuelSource
// implementation Pass 6's interface was waiting for. It swaps in
// WITHOUT editing any Pass 6 file: the Pass 6 driver only ever sees
// the interface, and the Pass 6 in-stock source stays as the
// fallback for a bakery that already holds fuel — it is NOT the
// production source, because a bakery that must be fed by a dealer
// (or an off-map import) cannot bake from an empty store.
//
// Units: dealer intake units and bakery fuel units are NOT assumed
// to be the same unit. The caller supplies IntakeUnitsPerBakeryUnit
// (>= 1); FuelUnitsOnHand is dealer stock divided by that factor,
// and SupplyTo dispenses exactly stillNeeded * factor intake units
// and receives exactly stillNeeded bakery units. Conversion across
// dispense lines carries the remainder forward line by line (so no
// intake unit is lost to rounding, and the final remainder is zero
// because the total dispensed is an exact multiple of the factor),
// with each bakery lot's provenance note naming its dealer line.
// The factor is a caller/test parameter, NEVER a decided game rate
// — in particular it must not be read as canonising Pass 7's adapter
// default (1 intake unit = 1 household unit), which is a different
// conversion (intake -> household bundle, not intake -> bakery fuel).
//
// Off-map fallback: when dealer stock cannot cover a request, an
// optional caller-supplied import callback may receive import lots
// into the fuel store through BakeryFuelSupply.ReceiveImportArrival
// (named import order + origin, never a conjured vendor). Without
// that callback, or when it refuses, SupplyTo refuses and the Pass 6
// driver refuses the bake — no fuel is conjured, and no bake runs
// on fuel that never arrived. Plain C#, Roslyn-testable.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.FreightFuel
{
    /// <summary>
    /// Caller-supplied off-map import receipt for the dealer fuel
    /// source: given the bakery fuel store, the bakery units still
    /// needed, the day, an id registry, and diagnostics, it receives
    /// import lots (named order + origin) and returns null on success
    /// or a refusal. Returning null without receiving anything is a
    /// caller error this source treats as a refusal, never as fuel.
    /// </summary>
    public delegate string Series2OffMapFuelReceipt(
        BakeryFuelStock stock, int bakeryUnitsNeeded, int dayIndex,
        EntityIdRegistry idRegistry, List<string> diag);

    /// <summary>Series 2 Pass 8: dealer (with optional off-map import) bakery fuel source.</summary>
    public sealed class Series2DealerBakeryFuelSource : ISeries2BakeryFuelSource
    {
        private readonly Series2FuelDealer dealer;

        /// <summary>
        /// Creates the source. <paramref name="intakeUnitsPerBakeryUnit"/>
        /// is a caller parameter (>= 1), never canon. <paramref name="offMapReceipt"/>
        /// may be null (dealer only); when supplied it is the ONLY
        /// off-map path, and it must name its order and origin.
        /// </summary>
        public Series2DealerBakeryFuelSource(
            Series2FuelDealer dealer,
            int intakeUnitsPerBakeryUnit = 1,
            Series2OffMapFuelReceipt offMapReceipt = null,
            EntityIdRegistry idRegistry = null)
        {
            this.dealer = dealer ?? throw new ArgumentNullException(nameof(dealer));
            IntakeUnitsPerBakeryUnit = Math.Max(1, intakeUnitsPerBakeryUnit);
            OffMapReceipt = offMapReceipt;
            IdRegistry = idRegistry;
        }

        /// <summary>Series 2 Pass 9, additive only: the dealer id, for save parameters. The source itself never owned an id before; this exposes the dealer's, never invents one.</summary>
        public string DealerBusinessId => dealer != null ? dealer.DealerBusinessId : string.Empty;

        public int IntakeUnitsPerBakeryUnit { get; }

        public Series2OffMapFuelReceipt OffMapReceipt { get; }

        public EntityIdRegistry IdRegistry { get; }

        public int FuelUnitsOnHand()
        {
            if (dealer == null) return 0;
            return dealer.StockUnits / IntakeUnitsPerBakeryUnit;
        }

        public string SupplyTo(BakeryShopRuntime bakery, int unitsNeeded, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (bakery == null || bakery.FuelStock == null)
                return "Series2DealerBakeryFuelSource: no bakery fuel store to supply.";
            if (unitsNeeded <= 0)
                return "Series2DealerBakeryFuelSource: a positive fuel quantity is required.";
            if (IdRegistry == null)
                return "Series2DealerBakeryFuelSource: no id registry; bakery lots need real ids.";
            int alreadyOnHand = bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);
            int stillNeeded = unitsNeeded - alreadyOnHand;
            if (stillNeeded <= 0)
            {
                diag.Add($"Series2DealerBakeryFuelSource: bakery fuel store already holds {alreadyOnHand}u, covers {unitsNeeded}u needed; no dealer movement required.");
                return null;
            }
            int dealerCoverable = FuelUnitsOnHand();
            if (OffMapReceipt == null && dealerCoverable < stillNeeded)
            {
                return $"Series2DealerBakeryFuelSource: dealer covers {dealerCoverable}u of {stillNeeded}u still needed, " +
                    $"bakery store held {alreadyOnHand}u, and no off-map receipt is wired — nothing dispensed, nothing baked from this supply.";
            }
            int fromDealer = Math.Min(stillNeeded, dealerCoverable);
            if (fromDealer > 0)
            {
                List<FuelYardByproductIntake.FuelYardDispenseLine> lines =
                    dealer.DispenseUnits(fromDealer * IntakeUnitsPerBakeryUnit, dayIndex, diag);
                if (lines == null)
                    return $"Series2DealerBakeryFuelSource: dealer dispense of {fromDealer * IntakeUnitsPerBakeryUnit}u refused — nothing supplied, nothing baked from this supply.";
                int carry = 0;
                int receivedBakeryUnits = 0;
                foreach (FuelYardByproductIntake.FuelYardDispenseLine line in lines)
                {
                    if (line == null || line.UnitsTaken <= 0) continue;
                    int available = line.UnitsTaken + carry;
                    int bakeryUnits = available / IntakeUnitsPerBakeryUnit;
                    carry = available % IntakeUnitsPerBakeryUnit;
                    if (bakeryUnits <= 0) continue;
                    string rejection = bakery.FuelStock.ReceiveLot(new BakeryFuelLot
                    {
                        LotId = IdRegistry.Allocate(EntityKind.Lot),
                        FuelName = BakeryBreadCatalog.FuelItemId,
                        Units = bakeryUnits,
                        AcquiredDayIndex = dayIndex,
                        FuelDealerBusinessId = dealer.DealerBusinessId,
                        SourceFuelLotId = line.LotId ?? string.Empty,
                        OriginName = $"FuelDealer {dealer.DealerBusinessId} (town fuel yard)",
                        SupplierNote = line.ProvenanceChain ?? string.Empty,
                        IsBootstrapEndowment = false,
                    }, diag);
                    if (rejection != null)
                    {
                        return $"Series2DealerBakeryFuelSource: bakery receipt failed after dealer dispense ({rejection}) — " +
                            $"dealer '{dealer.DealerBusinessId}' already dispensed {fromDealer * IntakeUnitsPerBakeryUnit}u; " +
                            "reconcile from diagnostics, do not re-dispense.";
                    }
                    receivedBakeryUnits += bakeryUnits;
                }
                if (carry != 0 || receivedBakeryUnits != fromDealer)
                {
                    return $"Series2DealerBakeryFuelSource: conversion invariant broken (received {receivedBakeryUnits}u of {fromDealer}u, carry {carry}) — " +
                        "reconcile from diagnostics, do not bake from this supply.";
                }
                stillNeeded -= fromDealer;
                diag.Add($"Series2DealerBakeryFuelSource: dealer '{dealer.DealerBusinessId}' supplied {fromDealer}u bakery fuel ({IntakeUnitsPerBakeryUnit} intake unit(s) per bakery unit); {stillNeeded}u still needed.");
            }
            if (stillNeeded <= 0) return null;
            if (OffMapReceipt == null)
            {
                return $"Series2DealerBakeryFuelSource: dealer covers {dealerCoverable}u of {unitsNeeded}u needed, " +
                    $"bakery store held {alreadyOnHand}u, {stillNeeded}u remain uncovered and no off-map receipt is wired — nothing baked from this supply.";
            }
            int before = bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);
            string importRefusal = OffMapReceipt(bakery.FuelStock, stillNeeded, dayIndex, IdRegistry, diag);
            int after = bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);
            if (importRefusal != null)
                return $"Series2DealerBakeryFuelSource: off-map fuel receipt refused ({importRefusal}); {stillNeeded}u remain uncovered — nothing baked from this supply.";
            if (after - before < stillNeeded)
                return $"Series2DealerBakeryFuelSource: off-map receipt added only {after - before}u of {stillNeeded}u still needed — nothing baked from this supply.";
            diag.Add($"Series2DealerBakeryFuelSource: off-map import covered the remaining {stillNeeded}u bakery fuel.");
            return null;
        }
    }
}
