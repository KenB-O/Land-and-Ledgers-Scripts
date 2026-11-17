// CAMPAIGN-B (DEF-001), package 17: bakery fuel conversion layer.
// Owner Decision 1 (Nov 15, 2026) settles the open fuel unit factors
// (D-2, D-F1, D-F3, D-F4) as provisional, researched calibration values:
//   1 dealer intake unit ~= 1 kg dry wood;
//   1 bakery fuel unit = 4 intake units (~4 kg per firing, 12 lb batch);
//   1 household retail bundle = 10 intake units;
//   sawmill-to-dealer byproduct price = 1c per intake unit;
//   retail anchors 22c offcuts / 28c fuel-wood bundles, per bundle.
// The conversions translate between unit systems; they never create stock.
// No fuel appears without production or a legitimate purchase.
//
// These tests execute the conversion authority directly (FuelConversionRates
// and FuelDealerByproductSettlement are plain C# and compile in the Roslyn
// harness) AND pin the harness-excluded wiring by source inspection, because
// SimulationDrivers and the fuel-dealer adapter cannot compile in the
// harness — the adapter's TryCreateForBusiness takes a BusinessInstanceState,
// whose closure runs through ScriptableObject and ~130 further types (the
// B-13 precedent). The wiring tests read the repo source, the B-13 pattern.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class CampaignBFuelConversionRatesTests
    {
        // --- D-F4: one intake unit ~= 1 kg dry wood ----------------------
        [Test]
        public void IntakeUnitIsOneKgDryWood()
        {
            Assert.AreEqual(1.0, FuelConversionRates.KgDryWoodPerDealerIntakeUnit, 0.0,
                "D-F4: one dealer intake unit is approximately 1 kg of dry-wood equivalent.");
        }

        // --- D-2: one bakery fuel unit = 4 intake units -------------------
        [Test]
        public void BakeryFuelUnitIsFourIntakeUnits()
        {
            Assert.AreEqual(4, FuelConversionRates.IntakeUnitsPerBakeryFuelUnit,
                "D-2: one bakery fuel unit = 4 intake units.");
            Assert.AreEqual(4, FuelConversionRates.IntakeUnitsForBakeryFuelUnits(1));
            Assert.AreEqual(12, FuelConversionRates.IntakeUnitsForBakeryFuelUnits(3));
            Assert.AreEqual(0, FuelConversionRates.IntakeUnitsForBakeryFuelUnits(0));
            Assert.AreEqual(0, FuelConversionRates.IntakeUnitsForBakeryFuelUnits(-2),
                "a negative quantity converts to zero; there is no negative fuel.");
        }

        [Test]
        public void IntakeUnitsConvertBackToBakeryFuelUnitsWithoutRoundingUp()
        {
            Assert.AreEqual(3, FuelConversionRates.BakeryFuelUnitsForIntakeUnits(12));
            Assert.AreEqual(3, FuelConversionRates.BakeryFuelUnitsForIntakeUnits(14),
                "a 2-unit remainder stays in the yard; it is never rounded up into a fuel unit the bakery does not have.");
            Assert.AreEqual(0, FuelConversionRates.BakeryFuelUnitsForIntakeUnits(3));
            Assert.AreEqual(0, FuelConversionRates.BakeryFuelUnitsForIntakeUnits(-5));
        }

        // --- D-F1: one household bundle = 10 intake units -----------------
        [Test]
        public void HouseholdBundleIsTenIntakeUnits()
        {
            Assert.AreEqual(10, FuelConversionRates.IntakeUnitsPerHouseholdBundle,
                "D-F1: one household retail bundle = 10 intake units.");
            Assert.AreEqual(10, FuelConversionRates.IntakeUnitsForHouseholdBundles(1));
            Assert.AreEqual(30, FuelConversionRates.IntakeUnitsForHouseholdBundles(3));
            Assert.AreEqual(0, FuelConversionRates.IntakeUnitsForHouseholdBundles(-1));
        }

        [Test]
        public void IntakeUnitsConvertBackToHouseholdBundlesWithoutRoundingUp()
        {
            Assert.AreEqual(2, FuelConversionRates.HouseholdBundlesForIntakeUnits(25),
                "a 5-unit remainder stays; a household cannot buy a bundle it has not paid for.");
            Assert.AreEqual(1, FuelConversionRates.HouseholdBundlesForIntakeUnits(10));
            Assert.AreEqual(0, FuelConversionRates.HouseholdBundlesForIntakeUnits(9));
        }

        // --- Firing consumption: ~4 kg per firing at the 12 lb batch ------
        [Test]
        public void ReferenceBatchBurnsFourKgPerFiring()
        {
            Assert.AreEqual(1, FuelConversionRates.BakeryFuelUnitsPerFiring(12.0),
                "12 lb batch at reference efficiency burns 1 bakery fuel unit = 4 intake units ~= 4 kg.");
            Assert.AreEqual(1, FuelConversionRates.BakeryFuelUnitsPerFiring(BakeryBreadCatalog.BreadFlourPerBatch),
                "the catalog's bread batch is the 12 lb reference batch.");
            double flourKg = BakeryBreadCatalog.BreadFlourPerBatch / FuelConversionRates.LbPerKg;
            double woodKgPerFlourKg = FuelConversionRates.DryWoodKgPerFiringAtReferenceBatch / flourKg;
            Assert.GreaterOrEqual(woodKgPerFlourKg, FuelConversionRates.DryWoodKgPerKgFlourLow,
                "4.0 kg of wood for 12 lb of flour must sit inside the researched 0.55-0.90 kg/kg range.");
            Assert.IsTrue(woodKgPerFlourKg <= FuelConversionRates.DryWoodKgPerKgFlourHigh,
                "4.0 kg of wood for 12 lb of flour must sit inside the researched 0.55-0.90 kg/kg range.");
        }

        [Test]
        public void FiringConsumptionScalesWithBatchSizeAndCeils()
        {
            Assert.AreEqual(1, FuelConversionRates.BakeryFuelUnitsPerFiring(6.0),
                "a half batch still burns a whole unit; a firing never under-fuels.");
            Assert.AreEqual(1, FuelConversionRates.BakeryFuelUnitsPerFiring(4.0));
            Assert.AreEqual(2, FuelConversionRates.BakeryFuelUnitsPerFiring(24.0),
                "a double batch burns two units.");
            Assert.AreEqual(3, FuelConversionRates.BakeryFuelUnitsPerFiring(36.0));
            Assert.AreEqual(2, FuelConversionRates.BakeryFuelUnitsPerFiring(13.0),
                "a batch just over the reference still ceils to two units.");
            Assert.AreEqual(0, FuelConversionRates.BakeryFuelUnitsPerFiring(0.0),
                "no batch, no firing.");
            Assert.AreEqual(0, FuelConversionRates.BakeryFuelUnitsPerFiring(-3.0));
        }

        [Test]
        public void FiringConsumptionAdjustsForOvenEfficiency()
        {
            Assert.AreEqual(2, FuelConversionRates.BakeryFuelUnitsPerFiring(12.0, 0.5),
                "a half-efficient oven (wet wood, cold start) burns twice the fuel.");
            Assert.AreEqual(1, FuelConversionRates.BakeryFuelUnitsPerFiring(12.0, 2.0),
                "a twice-efficient oven burns half the fuel, still a whole unit.");
            Assert.AreEqual(4, FuelConversionRates.BakeryFuelUnitsPerFiring(24.0, 0.5));
            Assert.IsTrue(ThrowsArgumentOutOfRange(() => FuelConversionRates.BakeryFuelUnitsPerFiring(12.0, 0.0)),
                "a non-positive efficiency is a caller error, not a firing.");
            Assert.IsTrue(ThrowsArgumentOutOfRange(() => FuelConversionRates.BakeryFuelUnitsPerFiring(12.0, -1.0)));
        }

        [Test]
        public void CatalogFiringConsumptionIsDerivedFromTheConversionAuthority()
        {
            Assert.AreEqual(
                FuelConversionRates.BakeryFuelUnitsPerFiring(BakeryBreadCatalog.BreadFlourPerBatch),
                BakeryBreadCatalog.FuelUnitsPerFiring,
                "the catalog's per-firing consumption is derived from FuelConversionRates, not a magic number.");
        }

        // --- D-F3: byproduct price 1c per intake unit ----------------------
        [Test]
        public void ByproductPriceIsOneCentPerIntakeUnit()
        {
            Assert.AreEqual(1, FuelConversionRates.SawmillByproductPriceCentsPerIntakeUnit,
                "D-F3: the initial sawmill-to-dealer byproduct price is 1c per intake unit.");
            Assert.AreEqual(1, FuelConversionRates.ByproductPriceCents(1));
            Assert.AreEqual(12, FuelConversionRates.ByproductPriceCents(12));
            Assert.AreEqual(0, FuelConversionRates.ByproductPriceCents(0));
            Assert.AreEqual(0, FuelConversionRates.ByproductPriceCents(-4));
        }

        // --- Retail anchors converted by quantity --------------------------
        [Test]
        public void RetailAnchorsArePerBundleAndConvertByQuantity()
        {
            Assert.AreEqual(28, FuelConversionRates.FuelWoodBundleRetailAnchorCents,
                "the 28c fuel-wood anchor is per household bundle.");
            Assert.AreEqual(22, FuelConversionRates.OffcutBundleRetailAnchorCents,
                "the 22c offcut anchor is per household bundle.");
            Assert.AreEqual(2.8, FuelConversionRates.RetailCentsPerIntakeUnit(28), 1e-9,
                "28c per 10-unit bundle is 2.8c per intake unit.");
            Assert.AreEqual(2.2, FuelConversionRates.RetailCentsPerIntakeUnit(22), 1e-9);
        }

        [Test]
        public void BakeryUnitPriceConvertsBundlePriceByQuantityNotByMultiplication()
        {
            Assert.AreEqual(11, FuelConversionRates.PriceCentsPerBakeryFuelUnit(28, 4),
                "28c per bundle -> 28 x 4 / 10 = 11c per bakery unit. The pre-package formula (28 x 4 = 112c) treated the bundle price as a per-intake-unit price and overcharged the bakery 10x.");
            Assert.AreEqual(9, FuelConversionRates.PriceCentsPerBakeryFuelUnit(22, 4),
                "22c per bundle -> 22 x 4 / 10 = 8.8c, rounded to the nearest cent.");
            Assert.AreEqual(3, FuelConversionRates.PriceCentsPerBakeryFuelUnit(28, 1),
                "28c per bundle -> 2.8c for a 1-intake-unit quantity, rounded to 3c.");
            Assert.AreEqual(0, FuelConversionRates.PriceCentsPerBakeryFuelUnit(0, 4));
            Assert.AreEqual(0, FuelConversionRates.PriceCentsPerBakeryFuelUnit(28, 0));
            Assert.AreEqual(0, FuelConversionRates.PriceCentsPerBakeryFuelUnit(-5, 4));
        }

        // --- D-F3 settlement: the dealer pays the mill ---------------------
        private sealed class FakeCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; set; }
            public readonly List<string> Entries = new List<string>();

            public string TryDebit(int cents, string reason)
            {
                if (cents < 0)
                {
                    return "cannot debit a negative amount.";
                }

                if (cents > BalanceCents)
                {
                    return "insufficient funds: " + BalanceCents + "c on hand, " + cents + "c requested.";
                }

                BalanceCents -= cents;
                Entries.Add("debit " + cents + "c: " + reason);
                return null;
            }

            public string Credit(int cents, string reason)
            {
                if (cents < 0)
                {
                    return "cannot credit a negative amount.";
                }

                BalanceCents += cents;
                Entries.Add("credit " + cents + "c: " + reason);
                return null;
            }
        }

        [Test]
        public void ByproductSettlementPaysMillAndDebitsDealer()
        {
            var dealer = new FakeCashPort { BalanceCents = 100 };
            var mill = new FakeCashPort { BalanceCents = 50 };
            var diag = new List<string>();

            var result = FuelDealerByproductSettlement.Settle(12, dealer, mill, diag);

            Assert.IsTrue(result.Settled, "refusal: " + result.Refusal);
            Assert.AreEqual(12, result.IntakeUnits);
            Assert.AreEqual(12, result.PriceCents, "12 intake units at 1c each.");
            Assert.AreEqual(88, dealer.BalanceCents, "the dealer paid 12c.");
            Assert.AreEqual(62, mill.BalanceCents, "the mill was credited 12c.");
            Assert.AreEqual(1, dealer.Entries.Count);
            Assert.AreEqual(1, mill.Entries.Count);
        }

        [Test]
        public void ByproductSettlementRefusesWhenDealerCannotPayAndMovesNothing()
        {
            var dealer = new FakeCashPort { BalanceCents = 5 };
            var mill = new FakeCashPort { BalanceCents = 50 };
            var diag = new List<string>();

            var result = FuelDealerByproductSettlement.Settle(12, dealer, mill, diag);

            Assert.IsFalse(result.Settled);
            Assert.IsTrue(result.Refusal.Length > 0, "a refusal names the blocker.");
            Assert.AreEqual(5, dealer.BalanceCents, "no money moved on a refused settlement.");
            Assert.AreEqual(50, mill.BalanceCents);
            Assert.AreEqual(0, dealer.Entries.Count);
            Assert.AreEqual(0, mill.Entries.Count);
        }

        [Test]
        public void ByproductSettlementOfZeroUnitsMovesNothing()
        {
            var dealer = new FakeCashPort { BalanceCents = 100 };
            var mill = new FakeCashPort { BalanceCents = 50 };

            var result = FuelDealerByproductSettlement.Settle(0, dealer, mill, new List<string>());

            Assert.IsTrue(result.Settled, "zero units settle vacuously.");
            Assert.AreEqual(0, result.PriceCents);
            Assert.AreEqual(100, dealer.BalanceCents);
            Assert.AreEqual(50, mill.BalanceCents);
        }

        [Test]
        public void ByproductSettlementRefusesWithoutCashPorts()
        {
            var dealer = new FakeCashPort { BalanceCents = 100 };
            var mill = new FakeCashPort { BalanceCents = 50 };

            var noDealer = FuelDealerByproductSettlement.Settle(12, null, mill, new List<string>());
            Assert.IsFalse(noDealer.Settled);
            Assert.AreEqual(100, dealer.BalanceCents, "nothing moved when the dealer port is missing.");

            var noMill = FuelDealerByproductSettlement.Settle(12, dealer, null, new List<string>());
            Assert.IsFalse(noMill.Settled);
            Assert.AreEqual(100, dealer.BalanceCents, "nothing moved when the mill port is missing.");
        }

        // The harness NUnit shim has no Assert.Throws; this try/catch form
        // compiles under both the shim and real NUnit.
        private static bool ThrowsArgumentOutOfRange(Action action)
        {
            try
            {
                action();
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }

            return false;
        }

        // --- Structural: harness-excluded wiring pins ----------------------
        // SimulationDrivers and the fuel-dealer adapter cannot compile in
        // the harness (BusinessInstanceState closure, B-13 precedent), so
        // these tests pin their wiring by reading the repo source.

        private const string AdapterPath =
            "Assets/Scripts/Domains/Economy/Businesses/FuelDealer/Series2FuelDealerSupplierAdapter.cs";
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string CatalogPath =
            "Assets/Scripts/Domains/Economy/Businesses/Bakery/BakeryBreadCatalog.cs";
        private const string DevConfigPath =
            "Assets/Core/Population/DevelopmentTownConfiguration.asset";
        private const string MediumConfigPath =
            "Assets/Core/Population/MediumDevelopmentTownConfiguration.asset";
        private const string LargeConfigPath =
            "Assets/Core/Population/LargeDevelopmentTownConfiguration.asset";

        [Test]
        public void Wiring_AdapterAppliesTheDecidedHouseholdFactor()
        {
            string code = ReadRepoFile(AdapterPath);
            Assert.IsTrue(code.Contains("FuelUnitsPerHouseholdUnit = FuelConversionRates.IntakeUnitsPerHouseholdBundle"),
                "TryCreateForBusiness must set the decided factor (10 intake units per bundle), not 0: " + AdapterPath);
            Assert.IsFalse(code.Contains("FuelUnitsPerHouseholdUnit = 0"),
                "the undecided 0 gate must be gone from the adapter: " + AdapterPath);
        }

        [Test]
        public void Wiring_DriverConvertsTheBakeryPriceByQuantity()
        {
            string code = ReadRepoFile(DriversPath);
            Assert.IsTrue(code.Contains("FuelConversionRates.PriceCentsPerBakeryFuelUnit"),
                "TryBuildPaidBakeryFuelSource must convert the bundle price by quantity (28c x 4 / 10 = 11c, not 28c x 4 = 112c): " + DriversPath);
            Assert.IsFalse(code.Contains("pricePerBakeryUnit = (long)dealerUnitPrice * intakeUnitsPerBakeryUnit"),
                "the old multiply-without-dividing formula must be gone: " + DriversPath);
        }

        [Test]
        public void Wiring_DriverSettlesTheByproductPurchase()
        {
            string code = ReadRepoFile(DriversPath);
            Assert.IsTrue(code.Contains("FuelDealerByproductSettlement.Settle"),
                "the sawmill transfer must settle the D-F3 payment on the actual moved delta: " + DriversPath);
            Assert.IsFalse(code.Contains("Custody only: never pays the mill (D-F3 open)"),
                "the custody-only docstring must be gone: D-F3 is decided: " + DriversPath);
        }

        [Test]
        public void Wiring_CatalogConsumptionIsDerivedFromTheConversionAuthority()
        {
            string code = ReadRepoFile(CatalogPath);
            Assert.IsTrue(code.Contains("FuelConversionRates.BakeryFuelUnitsPerFiring"),
                "BakeryBreadCatalog.FuelUnitsPerFiring must be derived from FuelConversionRates: " + CatalogPath);
            Assert.IsFalse(code.Contains("public const int FuelUnitsPerFiring"),
                "the magic-number const must be gone: " + CatalogPath);
        }

        [Test]
        public void Wiring_AssetsCarryTheDecidedBakeryFactor()
        {
            Assert.IsTrue(ReadRepoFile(DevConfigPath).Contains("fuelDealerIntakeUnitsPerBakeryUnit: 4"),
                "the dev scenario asset must carry the decided factor 4 in its fuel-dealer opening: " + DevConfigPath);
            // Schema 2 nests the factor in the fuel-dealer business's
            // opening record, and the medium and large scenarios have no
            // fuel-dealer business at all, so the factor has no home
            // there and is correctly absent rather than defaulted.
            Assert.IsFalse(ReadRepoFile(MediumConfigPath).Contains("operationId: fuel-dealer"),
                "the medium scenario has no fuel-dealer business, so no per-business factor is authored: " + MediumConfigPath);
            Assert.IsFalse(ReadRepoFile(LargeConfigPath).Contains("operationId: fuel-dealer"),
                "the large scenario has no fuel-dealer business, so no per-business factor is authored: " + LargeConfigPath);
        }

        // --- Repo-root discovery (B-13 pattern) ----------------------------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrEmpty(fromEnv) && File.Exists(Path.Combine(fromEnv, CatalogPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                System.IO.Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
            };
            var tried = new StringBuilder();
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (string candidate in new[]
                    {
                        dir,
                        Path.Combine(dir, "work-b"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, CatalogPath)))
                        {
                            return candidate;
                        }
                    }

                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign B (DEF-001): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign B (DEF-001): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }
    }
}
