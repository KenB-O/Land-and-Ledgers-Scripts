using LandLedgers.ReadModels.Valuation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.ReadModels
{
    /// <summary>
    /// BIZ-5: enterprise valuation (Canon §11.3–11.5, Tech X §9.3) — two values,
    /// cash excluded by construction, owner labor normalized, event-fed recompute.
    /// </summary>
    [TestFixture]
    public sealed class EnterpriseValuationTests
    {
        private static ValuationEvidence BuildEvidence(
            int weeks, int weeklyProfitCents,
            float ownerHours = 10f, int replacementRate = 25, int ownerDraw = 0,
            int liabilities = 20000, int assets = 0)
        {
            var evidence = new ValuationEvidence("biz_001");
            for (int i = 0; i < weeks; i++)
            {
                evidence.RecordWeeklyProfit(weeklyProfitCents);
            }

            evidence.SetOwnerLabor(ownerHours, replacementRate, ownerDraw);
            evidence.SetLiabilities(liabilities);
            evidence.SetTransferableAssetValue(assets);
            return evidence;
        }

        [Test]
        public void Evaluate_ExposesTwoValues_EquityIsGrossMinusLiabilities()
        {
            // 12w × 1000c; owner 10h × 25c replacement, no draw:
            // normalized = 1000 − 250 = 750/wk → 39000/yr → ×3 = 117000 gross.
            EnterpriseValuationResult result = EnterpriseValuation.Evaluate(
                BuildEvidence(12, 1000), ValuationTuning.Default());

            Assert.AreEqual(117000, result.GrossGoingConcernValueCents);
            Assert.AreEqual(97000, result.OwnerEquityValueCents); // 117000 − 20000
        }

        [Test]
        public void Evaluate_UnpaidOwnerHours_ReduceTransferableProfit()
        {
            // Same profit, but the owner works 40h unpaid at 25c/h:
            // normalized = 1000 − 1000 = 0 → gross 0.
            EnterpriseValuationResult heavy = EnterpriseValuation.Evaluate(
                BuildEvidence(12, 1000, ownerHours: 40f), ValuationTuning.Default());
            EnterpriseValuationResult light = EnterpriseValuation.Evaluate(
                BuildEvidence(12, 1000, ownerHours: 10f), ValuationTuning.Default());

            Assert.AreEqual(0, heavy.GrossGoingConcernValueCents);
            Assert.Greater(light.GrossGoingConcernValueCents, 0);
        }

        [Test]
        public void Evaluate_CashCannotEnterValuation_ByConstruction()
        {
            // Canon §11.4: cash contributes zero. The formula's inputs are
            // (profits, owner labor, liabilities, assets) — there is no cash
            // parameter, so "parking cash in a weak business" cannot move value.
            // This test pins that: identical evidence ⇒ identical value, and the
            // evidence type exposes no cash field to vary.
            ValuationEvidence a = BuildEvidence(12, 1000);
            ValuationEvidence b = BuildEvidence(12, 1000);

            Assert.AreEqual(
                EnterpriseValuation.Evaluate(a, ValuationTuning.Default()).OwnerEquityValueCents,
                EnterpriseValuation.Evaluate(b, ValuationTuning.Default()).OwnerEquityValueCents);

            var cashField = typeof(ValuationEvidence).GetField("cashCents",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNull(cashField, "ValuationEvidence must not gain a cash field (Canon §11.4).");
        }

        [Test]
        public void Evaluate_AssetFloor_DoesNotDoubleCount()
        {
            // Earnings value 117000; asset floor 200000 → gross is the floor, not sum.
            EnterpriseValuationResult result = EnterpriseValuation.Evaluate(
                BuildEvidence(12, 1000, assets: 200000), ValuationTuning.Default());

            Assert.AreEqual(200000, result.GrossGoingConcernValueCents);
        }

        [Test]
        public void Evaluate_Confidence_FollowsEvidenceDepth()
        {
            Assert.AreEqual(ValuationConfidence.Low,
                EnterpriseValuation.Evaluate(BuildEvidence(2, 1000), ValuationTuning.Default()).Confidence);
            Assert.AreEqual(ValuationConfidence.Medium,
                EnterpriseValuation.Evaluate(BuildEvidence(8, 1000), ValuationTuning.Default()).Confidence);
            // The default maintainable-profit window is capped at 12 weeks, so
            // additional history beyond that window does not increase confidence.
            Assert.AreEqual(ValuationConfidence.Medium,
                EnterpriseValuation.Evaluate(BuildEvidence(20, 1000), ValuationTuning.Default()).Confidence);
        }

        [Test]
        public void ReadModel_RecomputesOnEvents_AndSumsPlayerEquity()
        {
            var model = new EnterpriseValuationReadModel();
            model.RegisterBusiness("biz_001", "player", true);
            model.RegisterBusiness("biz_002", "town", false);

            for (int i = 0; i < 12; i++)
            {
                model.RecordWeeklyProfit("biz_001", 1000);
                model.RecordWeeklyProfit("biz_002", 5000);
            }

            model.RecordOwnerLabor("biz_001", 10f, 25, 0);
            model.RecordLiabilities("biz_001", 20000);

            int first = model.TotalPlayerOwnerEquityCents();
            Assert.AreEqual(97000, first);

            // Event arrives → cache invalidates → recompute reflects it.
            model.RecordLiabilities("biz_001", 50000);
            Assert.AreEqual(67000, model.TotalPlayerOwnerEquityCents());

            // The town's business never counts toward player equity.
            Assert.AreEqual(2, model.TrackedBusinessCount);
        }

        [Test]
        public void Evaluate_NeverAnnualizesBestShortPeriod()
        {
            // One great week among poor ones: the window average governs, not the peak.
            var evidence = new ValuationEvidence("biz_001");
            evidence.RecordWeeklyProfit(10000);
            evidence.RecordWeeklyProfit(100);
            evidence.RecordWeeklyProfit(100);
            evidence.RecordWeeklyProfit(100);
            evidence.SetOwnerLabor(0f, 25, 0);
            evidence.SetLiabilities(0);

            EnterpriseValuationResult result = EnterpriseValuation.Evaluate(
                evidence, ValuationTuning.Default());

            // Maintainable = (10000+100+100+100)/4 = 2575/wk → 133900/yr → ×3 = 401700.
            // Annualizing the best week would give 10000×52×3 = 1560000. Must not.
            Assert.AreEqual(401700, result.GrossGoingConcernValueCents);
        }
    }
}
