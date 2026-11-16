using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// DEF-013 (Decision 9): opening-economy audit, supply/demand price
    /// pressure, and the price functions the pressure feeds.
    /// </summary>
    [TestFixture]
    public sealed class OpeningEconomyDef013Tests
    {
        private static OpeningEconomyAudit.AuthoredOpening AuthoredDevOpening()
        {
            return new OpeningEconomyAudit.AuthoredOpening
            {
                BusinessCashTargets =
                {
                    ["general-store"] = 50000,
                    ["bakery"] = 35000,
                    ["fuel-dealer"] = 20000,
                    ["freight-company"] = 20000,
                    ["sawmill"] = 15000,
                    ["lumber-yard"] = 12000,
                },
                HouseholdCashTargets =
                {
                    ["base:2"] = 40000,
                    ["base:0"] = 40000,
                    ["base:4"] = 40000,
                    ["ethan"] = 40000,
                    ["shane"] = 40000,
                },
                OwnerStakeCents = 250000,
                AuthoredLiabilityCents = 0,
            };
        }

        private static OpeningEconomyAudit.ActualOpening ActualDevOpening()
        {
            return new OpeningEconomyAudit.ActualOpening
            {
                BusinessCash =
                {
                    ["general-store"] = 50000,
                    ["bakery"] = 35000,
                    ["fuel-dealer"] = 20000,
                    ["freight-company"] = 20000,
                    ["sawmill"] = 15000,
                    ["lumber-yard"] = 12000,
                },
                HouseholdCash =
                {
                    ["base:2"] = 40000,
                    ["base:0"] = 40000,
                    ["base:4"] = 40000,
                    ["ethan"] = 40000,
                    ["shane"] = 40000,
                },
                OwnerCashCents = 250000,
                LiabilityCents = 0,
            };
        }

        private static bool HasDiagnostic(List<string> diagnostics, string substring)
        {
            return diagnostics.Any(diagnostic => diagnostic.Contains(substring));
        }

        [Test]
        public void Audit_CleanOpening_HasNoDiagnostics()
        {
            List<string> diagnostics = OpeningEconomyAudit.Check(AuthoredDevOpening(), ActualDevOpening());
            Assert.AreEqual(0, diagnostics.Count, string.Join(" | ", diagnostics));
        }

        [Test]
        public void Audit_StoreBelowTarget_ReportsStoreAndTotal()
        {
            // The old New Game flow: the legacy migration moved the store's
            // surplus above the protected reserve into owner cash, so the
            // store opened below its authored 50000c.
            OpeningEconomyAudit.ActualOpening actual = ActualDevOpening();
            actual.BusinessCash["general-store"] = 25000;

            List<string> diagnostics = OpeningEconomyAudit.Check(AuthoredDevOpening(), actual);

            Assert.IsTrue(HasDiagnostic(diagnostics, "general-store"), string.Join(" | ", diagnostics));
            Assert.IsTrue(HasDiagnostic(diagnostics, "25000c"), string.Join(" | ", diagnostics));
            Assert.IsTrue(HasDiagnostic(diagnostics, "Total opening cash"), string.Join(" | ", diagnostics));
        }

        [Test]
        public void Audit_OwnerCashAboveStake_ReportsOwnerAndTotal()
        {
            // The old New Game flow: owner cash was the stake plus the
            // migrated store surplus, not the authored 250000c stake.
            OpeningEconomyAudit.ActualOpening actual = ActualDevOpening();
            actual.OwnerCashCents = 275000;

            List<string> diagnostics = OpeningEconomyAudit.Check(AuthoredDevOpening(), actual);

            Assert.IsTrue(HasDiagnostic(diagnostics, "Owner cash"), string.Join(" | ", diagnostics));
            Assert.IsTrue(HasDiagnostic(diagnostics, "275000c"), string.Join(" | ", diagnostics));
            Assert.IsTrue(HasDiagnostic(diagnostics, "Total opening cash"), string.Join(" | ", diagnostics));
        }

        [Test]
        public void Audit_NegativeBalance_ReportsNegative()
        {
            OpeningEconomyAudit.ActualOpening actual = ActualDevOpening();
            actual.HouseholdCash["ethan"] = -100;

            List<string> diagnostics = OpeningEconomyAudit.Check(AuthoredDevOpening(), actual);

            Assert.IsTrue(HasDiagnostic(diagnostics, "negative"), string.Join(" | ", diagnostics));
        }

        [Test]
        public void Audit_MissingBusiness_ReportsMissing()
        {
            OpeningEconomyAudit.ActualOpening actual = ActualDevOpening();
            actual.BusinessCash.Remove("sawmill");

            List<string> diagnostics = OpeningEconomyAudit.Check(AuthoredDevOpening(), actual);

            Assert.IsTrue(HasDiagnostic(diagnostics, "missing"), string.Join(" | ", diagnostics));
        }

        [Test]
        public void Audit_UnexpectedLiability_ReportsLiability()
        {
            OpeningEconomyAudit.ActualOpening actual = ActualDevOpening();
            actual.LiabilityCents = 5000;

            List<string> diagnostics = OpeningEconomyAudit.Check(AuthoredDevOpening(), actual);

            Assert.IsTrue(HasDiagnostic(diagnostics, "liabilities are 5000c"), string.Join(" | ", diagnostics));
        }

        [Test]
        public void Audit_NullInputs_AreRefusedNotSilent()
        {
            Assert.AreEqual(1, OpeningEconomyAudit.Check(null, ActualDevOpening()).Count);
            Assert.AreEqual(1, OpeningEconomyAudit.Check(AuthoredDevOpening(), null).Count);
        }

        [Test]
        public void Pressure_MapsStockHealthToScarcityPremium()
        {
            Assert.IsTrue(Math.Abs(RetailPricingDefinition.ResolveSupplyDemandPressure01(0f) - 1f) < 0.001f);
            Assert.IsTrue(Math.Abs(RetailPricingDefinition.ResolveSupplyDemandPressure01(0.25f) - 0.5f) < 0.001f);
            Assert.IsTrue(Math.Abs(RetailPricingDefinition.ResolveSupplyDemandPressure01(0.5f) - 0f) < 0.001f);
            Assert.IsTrue(Math.Abs(RetailPricingDefinition.ResolveSupplyDemandPressure01(0.75f) - (-0.5f)) < 0.001f);
            Assert.IsTrue(Math.Abs(RetailPricingDefinition.ResolveSupplyDemandPressure01(1f) - (-1f)) < 0.001f);
        }

        [Test]
        public void Pressure_ClampsOutOfRangeStockHealth()
        {
            Assert.IsTrue(Math.Abs(RetailPricingDefinition.ResolveSupplyDemandPressure01(-0.5f) - 1f) < 0.001f);
            Assert.IsTrue(Math.Abs(RetailPricingDefinition.ResolveSupplyDemandPressure01(1.5f) - (-1f)) < 0.001f);
        }

        private static RetailPricingDefinition Pricing(
            int baseline, int wholesale, int importCost,
            int bandMin, int bandMax, float sensitivity) => new RetailPricingDefinition
        {
            baselineCents = baseline,
            baseWholesaleCostCents = wholesale,
            importCostCents = importCost,
            localMarketBand = new PriceBand { minCents = bandMin, maxCents = bandMax },
            markupMultiplier = 1f,
            supplyDemandSensitivity = sensitivity,
        };

        [Test]
        public void CostPlusPrice_RisesWhenStockIsLow_AndFallsWhenStockIsFull()
        {
            // Landed 50c, sensitivity 0.5, wide band: no clamping in play.
            RetailPricingDefinition pricing = Pricing(100, 50, 0, 10, 200, 0.5f);

            Assert.AreEqual(50, pricing.GetCostPlusPriceCents(1f, 0f));
            Assert.AreEqual(75, pricing.GetCostPlusPriceCents(1f, 1f));
            Assert.AreEqual(25, pricing.GetCostPlusPriceCents(1f, -1f));
        }

        [Test]
        public void DisplayPrice_RisesWhenStockIsLow_AndFallsWhenStockIsFull()
        {
            RetailPricingDefinition pricing = Pricing(100, 50, 0, 10, 200, 0.5f);

            Assert.AreEqual(100, pricing.GetDisplayPriceCents(1f, 0f));
            Assert.AreEqual(150, pricing.GetDisplayPriceCents(1f, 1f));
            Assert.AreEqual(50, pricing.GetDisplayPriceCents(1f, -1f));
        }

        [Test]
        public void Price_ZeroSensitivity_NeverMovesWithPressure()
        {
            RetailPricingDefinition pricing = Pricing(100, 50, 0, 10, 200, 0f);

            Assert.AreEqual(50, pricing.GetCostPlusPriceCents(1f, 1f));
            Assert.AreEqual(50, pricing.GetCostPlusPriceCents(1f, -1f));
            Assert.AreEqual(100, pricing.GetDisplayPriceCents(1f, 1f));
            Assert.AreEqual(100, pricing.GetDisplayPriceCents(1f, -1f));
        }

        [Test]
        public void Price_PressureEffect_IsClampedByTheAuthoredBand()
        {
            RetailPricingDefinition pricing = Pricing(100, 50, 0, 10, 120, 0.5f);

            Assert.AreEqual(120, pricing.GetDisplayPriceCents(1f, 1f));
            Assert.AreEqual(100, pricing.GetDisplayPriceCents(1f, 0f));
        }
    }
}
