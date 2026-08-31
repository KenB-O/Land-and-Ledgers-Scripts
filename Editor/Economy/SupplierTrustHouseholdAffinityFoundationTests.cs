using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class SupplierTrustHouseholdAffinityFoundationTests
    {
        [Test]
        public void SupplierTrustIncreasesFromOnTimePaymentAndCleanFulfillment()
        {
            SupplierTrustEvaluator evaluator = new SupplierTrustEvaluator();
            SupplierRelationshipState supplier = CreateSupplier();

            SupplierTrustChangeProposal paid = evaluator.ProposeChange(supplier, CreateSupplierEvent(SupplierRelationshipEventType.InvoicePaidOnTime));
            SupplierRelationshipState afterPayment = evaluator.BuildUpdatedState(supplier, CreateSupplierEvent(SupplierRelationshipEventType.InvoicePaidOnTime));
            SupplierTrustChangeProposal fulfilled = evaluator.ProposeChange(afterPayment, CreateSupplierEvent(SupplierRelationshipEventType.OrderFulfilledClean));

            Assert.Greater(paid.ProjectedTrust01, supplier.trust01);
            Assert.Greater(paid.ProjectedPaymentHistory01, supplier.paymentHistory01);
            Assert.AreEqual(RelationshipTrendDirection.Improving, paid.Trend);
            CollectionAssert.Contains(paid.ReasonCodes, SupplierRelationshipReasonCode.OnTimePayment);
            Assert.Greater(fulfilled.ProjectedReliability01, afterPayment.reliability01);
            CollectionAssert.Contains(fulfilled.ReasonCodes, SupplierRelationshipReasonCode.CleanFulfillment);
        }

        [Test]
        public void SupplierTrustDecreasesFromLatePaymentAndFailedFulfillment()
        {
            SupplierTrustEvaluator evaluator = new SupplierTrustEvaluator();
            SupplierRelationshipState supplier = CreateSupplier();

            SupplierTrustChangeProposal late = evaluator.ProposeChange(supplier, CreateSupplierEvent(SupplierRelationshipEventType.InvoicePaidLate));
            SupplierTrustChangeProposal failed = evaluator.ProposeChange(supplier, CreateSupplierEvent(SupplierRelationshipEventType.OrderFailed));

            Assert.Less(late.ProjectedTrust01, supplier.trust01);
            Assert.Less(late.ProjectedPaymentHistory01, supplier.paymentHistory01);
            Assert.AreEqual(RelationshipTrendDirection.Declining, late.Trend);
            CollectionAssert.Contains(late.ReasonCodes, SupplierRelationshipReasonCode.LatePayment);
            Assert.Less(failed.ProjectedReliability01, supplier.reliability01);
            Assert.Greater(failed.ProjectedEmergencyOrderStrain01, supplier.emergencyOrderStrain01);
            CollectionAssert.Contains(failed.ReasonCodes, SupplierRelationshipReasonCode.FailedFulfillment);
        }

        [Test]
        public void SupplierEmergencyOrderAddsStrainAndGroundedReasons()
        {
            SupplierTrustEvaluator evaluator = new SupplierTrustEvaluator();
            SupplierRelationshipState supplier = CreateSupplier();

            SupplierTrustChangeProposal fulfilled = evaluator.ProposeChange(supplier, CreateSupplierEvent(SupplierRelationshipEventType.EmergencyOrderFulfilled));
            SupplierRelationshipState updated = evaluator.BuildUpdatedState(supplier, CreateSupplierEvent(SupplierRelationshipEventType.EmergencyOrderFulfilled));
            SupplierTrustStatusSummary summary = evaluator.Summarize(updated);

            Assert.Greater(fulfilled.ProjectedTrust01, supplier.trust01);
            Assert.Greater(fulfilled.ProjectedEmergencyOrderStrain01, supplier.emergencyOrderStrain01);
            Assert.AreEqual(1, updated.emergencyOrdersRequested);
            Assert.AreEqual(1, updated.emergencyOrdersFulfilled);
            CollectionAssert.Contains(fulfilled.ReasonCodes, SupplierRelationshipReasonCode.EmergencyOrderStrain);
            CollectionAssert.Contains(fulfilled.ReasonCodes, SupplierRelationshipReasonCode.EmergencyOrderRecovered);
            Assert.Greater(summary.SourcingReliability01, 0f);
        }

        [Test]
        public void SupplierTrustClampsAndSummariesUseDeterministicReasonOrder()
        {
            SupplierTrustEvaluator evaluator = new SupplierTrustEvaluator();
            SupplierRelationshipState strong = CreateSupplier();
            strong.trust01 = 0.98f;
            strong.reliability01 = 0.95f;
            strong.paymentHistory01 = 0.97f;
            strong.emergencyOrderStrain01 = 0f;
            strong.preferred = true;

            SupplierTrustChangeProposal granted = evaluator.ProposeChange(strong, CreateSupplierEvent(SupplierRelationshipEventType.PreferredStatusGranted));
            SupplierTrustStatusSummary summary = evaluator.Summarize(strong);

            Assert.LessOrEqual(granted.ProjectedTrust01, 1f);
            CollectionAssert.AreEqual(
                new[]
                {
                    SupplierRelationshipReasonCode.ReliableSourcing,
                    SupplierRelationshipReasonCode.PaymentHistoryStrong,
                    SupplierRelationshipReasonCode.TermsReady,
                    SupplierRelationshipReasonCode.PreferredSupplier
                },
                summary.ReasonCodes);
            Assert.IsTrue(summary.PreferredEligible);

            SupplierRelationshipState weak = CreateSupplier();
            weak.trust01 = 0.01f;
            weak.paymentHistory01 = 0.01f;
            weak.reliability01 = 0.01f;
            weak.emergencyOrderStrain01 = 0.99f;
            SupplierTrustChangeProposal missed = evaluator.ProposeChange(weak, CreateSupplierEvent(SupplierRelationshipEventType.InvoiceMissed));

            Assert.GreaterOrEqual(missed.ProjectedTrust01, 0f);
            Assert.GreaterOrEqual(missed.ProjectedPaymentHistory01, 0f);
            Assert.LessOrEqual(missed.ProjectedEmergencyOrderStrain01, 1f);
        }

        [Test]
        public void HouseholdAffinityIncreasesWithFairStockedRepeatPurchases()
        {
            HouseholdAffinityEvaluator evaluator = new HouseholdAffinityEvaluator();
            HouseholdBusinessAffinityState affinity = CreateAffinity();
            affinity.visitCount = 2;
            affinity.purchaseCount = 2;

            HouseholdAffinityChangeProposal proposal = evaluator.ProposeChange(affinity, CreateAffinityEvent(true, 160, 0.9f, 0.86f, 0.82f));
            HouseholdBusinessAffinityState updated = evaluator.BuildUpdatedState(affinity, CreateAffinityEvent(true, 160, 0.9f, 0.86f, 0.82f));

            Assert.Greater(proposal.ProjectedAffinity01, affinity.cachedAffinity01);
            Assert.AreEqual(RelationshipTrendDirection.Improving, proposal.Trend);
            CollectionAssert.Contains(proposal.ReasonCodes, HouseholdAffinityReasonCode.PurchaseCompleted);
            CollectionAssert.Contains(proposal.ReasonCodes, HouseholdAffinityReasonCode.RepeatedUse);
            CollectionAssert.Contains(proposal.ReasonCodes, HouseholdAffinityReasonCode.FairPrice);
            CollectionAssert.Contains(proposal.ReasonCodes, HouseholdAffinityReasonCode.ConsistentStock);
            CollectionAssert.Contains(proposal.ReasonCodes, HouseholdAffinityReasonCode.TrustedQuality);
            Assert.AreEqual(3, updated.visitCount);
            Assert.AreEqual(3, updated.purchaseCount);
            Assert.AreEqual(160, updated.totalSpendCents);
        }

        [Test]
        public void HouseholdAffinityDecreasesWithHighPriceStockoutAndNoPurchase()
        {
            HouseholdAffinityEvaluator evaluator = new HouseholdAffinityEvaluator();
            HouseholdBusinessAffinityState affinity = CreateAffinity();
            affinity.repeatedUse01 = 0.62f;
            affinity.priceFairnessMemory01 = 0.62f;
            affinity.stockConsistencyMemory01 = 0.62f;
            affinity.trustQualityMemory01 = 0.62f;
            affinity.cachedAffinity01 = evaluator.Summarize(affinity).Affinity01;

            HouseholdAffinityChangeProposal proposal = evaluator.ProposeChange(affinity, CreateAffinityEvent(false, 0, 0.12f, 0.18f, 0.24f));

            Assert.Less(proposal.ProjectedAffinity01, affinity.cachedAffinity01);
            Assert.AreEqual(RelationshipTrendDirection.Declining, proposal.Trend);
            CollectionAssert.Contains(proposal.ReasonCodes, HouseholdAffinityReasonCode.VisitWithoutPurchase);
            CollectionAssert.Contains(proposal.ReasonCodes, HouseholdAffinityReasonCode.HighPrice);
            CollectionAssert.Contains(proposal.ReasonCodes, HouseholdAffinityReasonCode.Stockout);
            CollectionAssert.Contains(proposal.ReasonCodes, HouseholdAffinityReasonCode.PoorQuality);
        }

        [Test]
        public void HouseholdAffinityDriftsTowardNeutralWithoutRouting()
        {
            HouseholdAffinityEvaluator evaluator = new HouseholdAffinityEvaluator();
            HouseholdBusinessAffinityState affinity = CreateAffinity();
            affinity.repeatedUse01 = 0.9f;
            affinity.priceFairnessMemory01 = 0.86f;
            affinity.stockConsistencyMemory01 = 0.84f;
            affinity.trustQualityMemory01 = 0.88f;
            affinity.cachedAffinity01 = evaluator.Summarize(affinity).Affinity01;
            affinity.lastVisitDayIndex = 0;

            HouseholdAffinityChangeProposal drift = evaluator.ProposeDrift(affinity, 60);

            Assert.Less(drift.ProjectedRepeatedUse01, affinity.repeatedUse01);
            Assert.Less(drift.ProjectedAffinity01, affinity.cachedAffinity01);
            Assert.AreEqual(RelationshipTrendDirection.Declining, drift.Trend);
            CollectionAssert.Contains(drift.ReasonCodes, HouseholdAffinityReasonCode.AffinityDrift);
            CollectionAssert.DoesNotContain(drift.ReasonCodes, HouseholdAffinityReasonCode.VisitWithoutPurchase);
        }

        [Test]
        public void GeneralStoreAffinityDriftsMoreSlowlyThanNonRoutineTrade()
        {
            HouseholdAffinityEvaluator evaluator = new HouseholdAffinityEvaluator();

            HouseholdBusinessAffinityState generalStore = CreateAffinity();
            generalStore.repeatedUse01 = 0.84f;
            generalStore.priceFairnessMemory01 = 0.82f;
            generalStore.stockConsistencyMemory01 = 0.81f;
            generalStore.trustQualityMemory01 = 0.8f;
            generalStore.cachedAffinity01 = evaluator.Summarize(generalStore).Affinity01;
            generalStore.lastVisitDayIndex = 0;

            HouseholdBusinessAffinityState blacksmith = CreateAffinity();
            blacksmith.businessId = "wright_blacksmith";
            blacksmith.businessType = BusinessType.Blacksmith;
            blacksmith.repeatedUse01 = generalStore.repeatedUse01;
            blacksmith.priceFairnessMemory01 = generalStore.priceFairnessMemory01;
            blacksmith.stockConsistencyMemory01 = generalStore.stockConsistencyMemory01;
            blacksmith.trustQualityMemory01 = generalStore.trustQualityMemory01;
            blacksmith.cachedAffinity01 = evaluator.Summarize(blacksmith).Affinity01;
            blacksmith.lastVisitDayIndex = 0;

            HouseholdAffinityChangeProposal generalStoreDrift = evaluator.ProposeDrift(generalStore, 45);
            HouseholdAffinityChangeProposal blacksmithDrift = evaluator.ProposeDrift(blacksmith, 45);

            Assert.Greater(generalStoreDrift.ProjectedAffinity01, blacksmithDrift.ProjectedAffinity01);
            Assert.Greater(generalStoreDrift.ProjectedRepeatedUse01, blacksmithDrift.ProjectedRepeatedUse01);
        }

        [Test]
        public void HouseholdAffinityClampsAndSummariesUseDeterministicReasonOrder()
        {
            HouseholdAffinityEvaluator evaluator = new HouseholdAffinityEvaluator();
            HouseholdBusinessAffinityState strong = CreateAffinity();
            strong.repeatedUse01 = 0.97f;
            strong.priceFairnessMemory01 = 0.96f;
            strong.stockConsistencyMemory01 = 0.95f;
            strong.trustQualityMemory01 = 0.94f;
            strong.cachedAffinity01 = evaluator.Summarize(strong).Affinity01;

            HouseholdAffinityChangeProposal high = evaluator.ProposeChange(strong, CreateAffinityEvent(true, 200, 1f, 1f, 1f));
            HouseholdAffinityScoreSummary summary = evaluator.Summarize(strong);

            Assert.LessOrEqual(high.ProjectedAffinity01, 1f);
            CollectionAssert.AreEqual(
                new[]
                {
                    HouseholdAffinityReasonCode.PreferenceStrength,
                    HouseholdAffinityReasonCode.RepeatedUse,
                    HouseholdAffinityReasonCode.FairPrice,
                    HouseholdAffinityReasonCode.ConsistentStock,
                    HouseholdAffinityReasonCode.TrustedQuality
                },
                summary.ReasonCodes);
            Assert.Greater(summary.PreferenceWeight01, 0.5f);

            HouseholdBusinessAffinityState weak = CreateAffinity();
            weak.repeatedUse01 = 0.01f;
            weak.priceFairnessMemory01 = 0.01f;
            weak.stockConsistencyMemory01 = 0.01f;
            weak.trustQualityMemory01 = 0.01f;
            HouseholdAffinityChangeProposal low = evaluator.ProposeChange(weak, CreateAffinityEvent(false, 0, 0f, 0f, 0f));

            Assert.GreaterOrEqual(low.ProjectedRepeatedUse01, 0f);
            Assert.GreaterOrEqual(low.ProjectedAffinity01, 0f);
        }

        private static SupplierRelationshipState CreateSupplier()
        {
            return new SupplierRelationshipState
            {
                supplierId = "regional_wholesaler",
                displayName = "Regional Wholesaler",
                originKind = SupplierOriginKind.RegionalOffMap,
                trust01 = 0.5f,
                reliability01 = 0.5f,
                paymentHistory01 = 0.5f,
                emergencyOrderStrain01 = 0.1f
            };
        }

        private static SupplierRelationshipEvent CreateSupplierEvent(SupplierRelationshipEventType eventType)
        {
            return new SupplierRelationshipEvent
            {
                supplierId = "regional_wholesaler",
                eventType = eventType,
                absoluteDayIndex = 12,
                severity01 = 1f
            };
        }

        private static HouseholdBusinessAffinityState CreateAffinity()
        {
            return new HouseholdBusinessAffinityState
            {
                householdId = 7,
                businessId = "miller_general_store",
                hasBusinessType = true,
                businessType = BusinessType.GeneralStore,
                repeatedUse01 = 0.5f,
                priceFairnessMemory01 = 0.5f,
                stockConsistencyMemory01 = 0.5f,
                trustQualityMemory01 = 0.5f,
                cachedAffinity01 = 0.5f,
                lastVisitDayIndex = 10
            };
        }

        private static HouseholdAffinityEvent CreateAffinityEvent(bool completedPurchase, int spendCents, float priceFairness, float stockConsistency, float trustQuality)
        {
            return new HouseholdAffinityEvent
            {
                householdId = 7,
                businessId = "miller_general_store",
                hasBusinessType = true,
                businessType = BusinessType.GeneralStore,
                completedPurchase = completedPurchase,
                spendCents = spendCents,
                priceFairness01 = priceFairness,
                stockConsistency01 = stockConsistency,
                trustQuality01 = trustQuality,
                absoluteDayIndex = 12
            };
        }
    }
}
