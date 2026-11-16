// Land & Ledgers — fuel unit conversions (owner Decision 1, Nov 15, 2026).
//
// DEF-001 conversion layer. Before this package the fuel unit factors were
// open owner decisions (D-2, D-F1, D-F3, D-F4) and the bakery's 40-unit
// opening stock was a hard countdown with no replenishment path. Decision 1
// settles them as PROVISIONAL, researched calibration values — not historical
// facts and not mandatory global rates. They are the starting values the
// simulation calibrates against, and prices subsequently respond to actual
// costs, inventory, competition, and demand.
//
// The single rule: no fuel may appear without production or a legitimate
// purchase. These conversions only translate between unit systems; they
// never create stock. Stock still comes from the sawmill's byproducts, a
// dealer's purchase, or a household's purchase — never from a conversion.
//
// Unit systems (all three are masses of dry wood, counted differently):
//   - dealer intake unit: the sawmill-to-dealer custody unit, ~= 1 kg dry wood.
//   - bakery fuel unit: the unit the bakery burns per firing, 4 intake units.
//   - household retail bundle: the unit a household buys at retail, 10 intake units.
// The 22c offcut / 28c fuel-wood retail anchors are per BUNDLE, so a price
// converted to another unit must divide by the bundle size first — treating
// a bundle price as a per-intake-unit price overcharges 10x (that was the
// live bug in SimulationDrivers.TryBuildPaidBakeryFuelSource before this
// package: 28c x 4 = 112c per bakery unit instead of 28c x 4 / 10 = 11c).

using System;

namespace LandLedgers.Economy
{
    public static class FuelConversionRates
    {
        // --- Mass basis (Decision 1) -------------------------------------
        // One dealer intake unit is approximately 1 kg of dry-wood equivalent.
        public const double KgDryWoodPerDealerIntakeUnit = 1.0;

        // --- Unit factors (Decision 1) ------------------------------------
        // One bakery fuel unit = 4 intake units (~4 kg dry wood).
        public const int IntakeUnitsPerBakeryFuelUnit = 4;

        // One household retail bundle = 10 intake units (~10 kg dry wood).
        public const int IntakeUnitsPerHouseholdBundle = 10;

        // --- Sawmill byproduct price (Decision 1, D-F3) -------------------
        // Initial sawmill-to-dealer byproduct purchase price: 1 cent per
        // intake unit, before transport and processing. The mill is PAID
        // this price; the transfer is a purchase, not a gift of custody.
        public const int SawmillByproductPriceCentsPerIntakeUnit = 1;

        // --- Retail anchors (Decision 1) -----------------------------------
        // Existing scenario retail anchors, per household bundle. They are
        // anchors, not fixed prices: prices subsequently respond to actual
        // costs, inventory, competition, and demand.
        public const int FuelWoodBundleRetailAnchorCents = 28;
        public const int OffcutBundleRetailAnchorCents = 22;

        // --- Firing consumption (Decision 1) ------------------------------
        // Research on wood-fired ovens suggests approximately 0.55-0.90 kg
        // of wood per kg of flour baked. For the current 12 lb flour batch
        // the reference consumption is approximately 4 kg of dry-wood
        // equivalent per firing. 12 lb = 5.443 kg of flour, so 4.0 kg is
        // 0.735 kg of wood per kg of flour — inside the researched range.
        public const double DryWoodKgPerKgFlourLow = 0.55;
        public const double DryWoodKgPerKgFlourHigh = 0.90;
        public const double ReferenceFlourLbPerBatch = 12.0;
        public const double DryWoodKgPerFiringAtReferenceBatch = 4.0;

        // Pounds per kilogram, for the flour-lb to flour-kg conversion.
        public const double LbPerKg = 2.20462;

        // --- Bakery <-> intake --------------------------------------------
        public static int IntakeUnitsForBakeryFuelUnits(int bakeryUnits)
        {
            if (bakeryUnits <= 0)
            {
                return 0;
            }

            return bakeryUnits * IntakeUnitsPerBakeryFuelUnit;
        }

        // Whole bakery fuel units in an intake quantity. A remainder stays
        // in the yard as intake units; it is never rounded up into a fuel
        // unit the bakery does not have.
        public static int BakeryFuelUnitsForIntakeUnits(int intakeUnits)
        {
            if (intakeUnits <= 0)
            {
                return 0;
            }

            return intakeUnits / IntakeUnitsPerBakeryFuelUnit;
        }

        // --- Household bundle <-> intake ----------------------------------
        public static int IntakeUnitsForHouseholdBundles(int bundles)
        {
            if (bundles <= 0)
            {
                return 0;
            }

            return bundles * IntakeUnitsPerHouseholdBundle;
        }

        // Whole household bundles in an intake quantity (remainder stays).
        public static int HouseholdBundlesForIntakeUnits(int intakeUnits)
        {
            if (intakeUnits <= 0)
            {
                return 0;
            }

            return intakeUnits / IntakeUnitsPerHouseholdBundle;
        }

        // --- Firing consumption --------------------------------------------
        // Bakery fuel units burned per firing for a batch of flourLbPerBatch
        // pounds of flour, at the given oven efficiency (1.0 = the reference
        // oven; lower = less efficient — wet wood, poor draught, cold start —
        // and burns proportionally more; higher = a more efficient oven).
        // Moisture and firing schedule are folded into the efficiency
        // multiplier: a firing that starts cold or burns wet wood is simply
        // a less efficient firing. Consumption scales with batch size and
        // ceils to a whole fuel unit, so a firing never under-fuels.
        public static int BakeryFuelUnitsPerFiring(double flourLbPerBatch, double ovenEfficiency = 1.0)
        {
            if (flourLbPerBatch <= 0)
            {
                return 0;
            }

            if (ovenEfficiency <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ovenEfficiency),
                    "Oven efficiency must be positive (1.0 = reference; lower = less efficient, more fuel).");
            }

            double dryWoodKg = DryWoodKgPerFiringAtReferenceBatch
                * (flourLbPerBatch / ReferenceFlourLbPerBatch)
                / ovenEfficiency;
            double intakeUnits = dryWoodKg / KgDryWoodPerDealerIntakeUnit;
            double bakeryUnits = intakeUnits / IntakeUnitsPerBakeryFuelUnit;
            // The 1e-9 guard keeps exact reference values (12 lb -> exactly
            // 1.0) from rounding up on floating-point noise; a genuinely
            // fractional value still rounds up to the next whole unit.
            return Math.Max(1, (int)Math.Ceiling(bakeryUnits - 1e-9));
        }

        // --- Prices ----------------------------------------------------------
        // Sawmill-to-dealer byproduct price for a quantity of intake units.
        public static int ByproductPriceCents(int intakeUnits)
        {
            if (intakeUnits <= 0)
            {
                return 0;
            }

            return intakeUnits * SawmillByproductPriceCentsPerIntakeUnit;
        }

        // Price per bakery fuel unit, converted by quantity from a price
        // quoted per household bundle. The bundle price is divided by the
        // 10-intake-unit bundle size FIRST, then multiplied by the 4
        // intake units in a bakery unit: 28c per bundle -> 28 x 4 / 10 =
        // 11c per bakery unit. (Multiplying without dividing — 28c x 4 =
        // 112c — treats the bundle price as a per-intake-unit price and
        // overcharges the bakery 10x.) Rounded to the nearest cent, the
        // codebase's established price pattern.
        public static long PriceCentsPerBakeryFuelUnit(int bundlePriceCents, int intakeUnitsPerBakeryUnit)
        {
            if (bundlePriceCents <= 0 || intakeUnitsPerBakeryUnit <= 0)
            {
                return 0;
            }

            double perUnit = (double)bundlePriceCents * intakeUnitsPerBakeryUnit / IntakeUnitsPerHouseholdBundle;
            return (long)Math.Round(perUnit, MidpointRounding.AwayFromZero);
        }

        // Price per intake unit implied by a per-bundle retail price
        // (28c per bundle = 2.8c per intake unit). For diagnostics and
        // price-band reasoning only; actual charges stay in whole cents.
        public static double RetailCentsPerIntakeUnit(int bundlePriceCents)
        {
            if (bundlePriceCents <= 0)
            {
                return 0;
            }

            return (double)bundlePriceCents / IntakeUnitsPerHouseholdBundle;
        }
    }
}
