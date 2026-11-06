using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 10 slice E4 (Obj 8): fuel-blocker diagnostics + the authorable
    /// D-2 factor field. The line names the exact missing piece(s) from
    /// observed state and never claims a fuel unit is supplied. No test
    /// here asserts fuel is supplied — that is B1's work, blocked on
    /// D-2/D-7.
    /// </summary>
    [TestFixture]
    public sealed class BakeryFuelBlockerDiagnosticsTests
    {
        private static BakeryFuelBlockerInputs Inputs(
            int batches = 1, int onHand = 0, int? factor = null,
            bool intake = false, bool dealerAttached = false, int dealerUnits = 0) =>
            new BakeryFuelBlockerInputs
            {
                BatchesPlanned = batches,
                FuelUnitsOnHand = onHand,
                AuthoredFactor = factor,
                YardIntakeExistsInDailyFlow = intake,
                DealerSourceAttached = dealerAttached,
                DealerFuelUnitsOnHand = dealerUnits,
            };

        [Test]
        public void NoFactor_NoIntake_NoDealer_NamesEveryMissingPiece()
        {
            string line = BakeryFuelBlockerDiagnostics.BuildFuelLine(Inputs());

            Assert.IsTrue(line.Contains("BLOCKED"), "line names the block; line: " + line);
            Assert.IsTrue(line.Contains("fuel 0u on hand, need 1u for 1 batch(es)"),
                "line names need vs on hand; line: " + line);
            Assert.IsTrue(line.Contains("fuel store short by 1u"), "line: " + line);
            Assert.IsTrue(line.Contains("factor unset (D-2)"), "line: " + line);
            Assert.IsTrue(line.Contains("no live fuel-yard intake in the First Ledger daily flow"), "line: " + line);
            Assert.IsTrue(line.Contains("no dealer fuel source attached"), "line: " + line);
            Assert.IsTrue(line.Contains("D-2") && line.Contains("D-7"),
                "line names the open decisions; line: " + line);
        }

        [Test]
        public void FactorAuthored_NoIntake_StillRefusesToClaimSupply()
        {
            string line = BakeryFuelBlockerDiagnostics.BuildFuelLine(Inputs(factor: 4));

            Assert.IsTrue(line.Contains("BLOCKED"), "line: " + line);
            Assert.IsTrue(line.Contains("factor authored (4 intake units per bakery unit)"), "line: " + line);
            Assert.IsTrue(line.Contains("no live fuel-yard intake"), "line: " + line);
            Assert.IsFalse(line.Contains("fuel sufficient"),
                "an authored factor alone must never claim supply; line: " + line);
        }

        [Test]
        public void FactorZero_TreatedAsUnset_NeverAsOne()
        {
            string line = BakeryFuelBlockerDiagnostics.BuildFuelLine(Inputs(factor: 0));

            Assert.IsTrue(line.Contains("factor unset (D-2)"),
                "a factor of 0 is unset, never 1; line: " + line);
            Assert.IsFalse(line.Contains("factor authored"), "line: " + line);
        }

        [Test]
        public void StockPresent_NamesNoBlocker()
        {
            string line = BakeryFuelBlockerDiagnostics.BuildFuelLine(Inputs(onHand: 2));

            Assert.IsTrue(line.Contains("fuel sufficient"), "line: " + line);
            Assert.IsFalse(line.Contains("BLOCKED"), "line: " + line);
            Assert.IsTrue(line.Contains("2u on hand, need 1u"), "line: " + line);
        }

        [Test]
        public void AllPresentButShort_NamesOnlyTheShortfall()
        {
            string line = BakeryFuelBlockerDiagnostics.BuildFuelLine(
                Inputs(factor: 4, intake: true, dealerAttached: true, dealerUnits: 0));

            Assert.IsTrue(line.Contains("BLOCKED"), "line: " + line);
            Assert.IsTrue(line.Contains("fuel store short by 1u"), "line: " + line);
            Assert.IsFalse(line.Contains("factor unset"), "the factor is authored; line: " + line);
            Assert.IsFalse(line.Contains("no live fuel-yard intake"), "the intake is present; line: " + line);
            Assert.IsFalse(line.Contains("no dealer fuel source attached"), "the dealer is attached; line: " + line);
        }

        [Test]
        public void NoBatchesPlanned_NoFuelNeeded()
        {
            string line = BakeryFuelBlockerDiagnostics.BuildFuelLine(Inputs(batches: 0));
            Assert.IsTrue(line.Contains("no batches planned today — no fuel needed"), "line: " + line);
            Assert.IsFalse(line.Contains("BLOCKED"), "line: " + line);
        }

        [Test]
        public void ConfigDefault_FuelDealerFactorIsUnset()
        {
            DevelopmentTownConfiguration configuration =
                ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            Assert.AreEqual(0, configuration.FuelDealerIntakeUnitsPerBakeryUnit,
                "the D-2 factor is zero/unset by default — no dealer conversion until Kennedy supplies the value");
        }
    }
}
