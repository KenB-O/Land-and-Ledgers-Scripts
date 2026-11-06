// Series 2 Pass 8 (freight + fuel chain): the dealer-owned fuel callable.
// This is the Fuel Dealer runtime surface Pass 7 deliberately did NOT
// create: Pass 7's adapter is the selling surface over intake lots
// that already exist; this class owns the intake itself — sawmill
// byproduct receipt, stock, and dispense to bakery and households.
//
// Selling is NOT duplicated here: a household retail sale goes through
// the Pass 7 Series2FuelDealerSupplierAdapter (which dispenses and
// posts revenue, with its WARNING-only cash pattern documented in the
// Pass 8 review), and a carrier leg's freight charge is settled by
// Series2CarrierSettlement on delivery acceptance only. This class
// moves custody only, so the same fuel is never sold twice by two
// different sale paths in one call. A caller that wants a paid
// dealer-to-bakery transfer settles it with the carrier settlement
// (or the wholesale settlement for a wholesale leg) after this
// class has moved the stock — never inside this class, and never
// through HouseholdLedger (business-to-business cash is not
// household cash; see the Pass 7 brief correction).
//
// Sawmill price: the byproduct transfer currently pays the sawmill
// NOTHING. That is not a decision smuggled in as zero: ReceiveSawmillLot
// moves custody only and diagnoses, in plain text, that no sawmill
// payment was made because the price, if any, is a Kennedy decision.
// If Kennedy later sets a price, a caller settles it through the
// business cash ports; this class must not be edited to invent one.
//
// Dispense semantics are the intake's (Pass 2 API), never re-invented:
// FIFO with provenance, shortfall dispenses NOTHING, emptied lots
// are removed and never re-sold. SupplyFuelToBakery receives one
// bakery lot per dispense line, so provenance survives lot by lot;
// a bakery-lot receipt failure after a dispense is diagnosed with
// the lot ids and units, because the two stores cannot be rolled
// back as one transaction — in practice the receipt cannot fail
// for a fresh lot id, a named dealer, and positive units, which
// this class pre-validates before dispensing. Plain C#, no Unity
// dependency, Roslyn-testable.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.FreightFuel
{
    /// <summary>Series 2 Pass 8: dealer-owned fuel custody over the real intake.</summary>
    public sealed class Series2FuelDealer
    {
        private readonly FuelYardByproductIntake intake;

        public Series2FuelDealer(FuelYardByproductIntake intake, string dealerBusinessId)
        {
            this.intake = intake ?? throw new ArgumentNullException(nameof(intake));
            DealerBusinessId = dealerBusinessId ?? string.Empty;
        }

        public string DealerBusinessId { get; }

        public FuelYardByproductIntake Intake => intake;

        public int StockUnits => intake.TotalFuelWoodUnitsFor(DealerBusinessId);

        /// <summary>
        /// Receives one sawmill byproduct lot into this dealer's yard.
        /// Custody only: NO cash moves to the sawmill here, and the
        /// diagnostic says so, because the sawmill price (if any) is
        /// an open Kennedy decision, not a zero price. Returns null
        /// on success, or the intake's refusal.
        /// </summary>
        public string ReceiveSawmillLot(SlabOffcutFuelLot lot, List<string> diag)
        {
            diag = diag ?? new List<string>();
            string refusal = intake.ReceiveByproductLot(DealerBusinessId, lot, diag);
            if (refusal != null) return refusal;
            diag.Add($"Series2FuelDealer: sawmill lot received into '{DealerBusinessId}' custody; " +
                "no sawmill payment made here — byproduct price is an open Kennedy decision, not 0c by default.");
            return null;
        }

        /// <summary>
        /// Dispenses fuel-wood units FIFO with provenance. Returns null
        /// on shortfall/unknown dealer, in which case NOTHING is
        /// dispensed (the intake's own guarantee, relied on here).
        /// This is custody movement only, not a sale.
        /// </summary>
        public List<FuelYardByproductIntake.FuelYardDispenseLine> DispenseUnits(
            int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (units <= 0)
            {
                diag.Add("Series2FuelDealer: dispense needs a positive unit count; nothing dispensed.");
                return null;
            }
            List<FuelYardByproductIntake.FuelYardDispenseLine> lines =
                intake.TryDispenseUnits(DealerBusinessId, units, dayIndex, diag);
            if (lines == null || lines.Count == 0)
            {
                diag.Add($"Series2FuelDealer: dispense of {units}u refused by the intake; nothing dispensed, no sale faked.");
                return null;
            }
            return lines;
        }

    }
}
