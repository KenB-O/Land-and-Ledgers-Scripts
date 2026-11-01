using LandLedgers.Economy.DealTerms;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Valuation;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.DealTerms
{
    public sealed class ExpandedDealTermFoundationTests
    {
        [Test]
        public void SellerFinancingSanitization_ClampsPrincipalToPriceNetOfDownPayment()
        {
            SellerFinancingTerms sanitized = new SellerFinancingTerms
            {
                offered = true,
                principalCents = 400000,
                downPaymentCents = 200000,
                balloonPaymentCents = 350000,
                termMonths = 48
            }.Sanitized(500000);

            Assert.AreEqual(300000, sanitized.principalCents);
            Assert.AreEqual(300000, sanitized.balloonPaymentCents);
        }

        [Test]
        public void SellerFinancingSanitization_RemovesOfferedState_WhenNoFinancedPrincipalRemains()
        {
            SellerFinancingTerms sanitized = new SellerFinancingTerms
            {
                offered = true,
                downPaymentCents = 500000,
                termMonths = 48,
                balloonPaymentCents = 100000
            }.Sanitized(500000);

            Assert.IsFalse(sanitized.offered);
            Assert.AreEqual(0, sanitized.balloonPaymentCents);
        }

        [Test]
        public void PartialInventorySanitization_AddsBalancedDefaults_WhenAuthoringIsThin()
        {
            InventoryTransferTerms sanitized = new InventoryTransferTerms
            {
                transferMode = InventoryTransferMode.Partial,
                estimatedInventoryValueCents = 60000
            }.Sanitized();

            Assert.AreEqual(0.35f, sanitized.operatingContinuity01, 0.0001f);
            Assert.AreEqual(0.35f, sanitized.restockBurden01, 0.0001f);
            Assert.AreEqual(0.2f, sanitized.verificationRisk01, 0.0001f);
        }

        [Test]
        public void ComplexExpandedTerms_DefaultDocumentationQualityToNeutralInsteadOfZero()
        {
            ExpandedDealTerms sanitized = new ExpandedDealTerms
            {
                retainedLandLease = new RetainedLandLeaseTerms
                {
                    retainedLandLease = true,
                    businessOnlyTransfer = true,
                    weeklyLeasePaymentCents = 7000,
                    leaseTermMonths = 12,
                    renewalSecurity01 = 0.2f,
                    sellerControlRights01 = 0.8f
                },
                delayedPossession = new DelayedPossessionTerms
                {
                    delayed = true,
                    minimumDelayDays = 14,
                    maximumDelayDays = 28,
                    source = DelayedPossessionSource.SellerRequested,
                    sellerContinuityNeed01 = 0.5f
                }
            }.Sanitized(500000);

            Assert.AreEqual(0.72f, sanitized.documentationQuality01, 0.0001f);
        }

        [Test]
        public void OmittedDocumentationOnComplexTerms_DoesNotAutoFlagWeakPaperwork()
        {
            ExpandedDealTermEvaluationResult result = new ExpandedDealTermEvaluator().Evaluate(new ExpandedDealTerms
            {
                retainedLandLease = new RetainedLandLeaseTerms
                {
                    retainedLandLease = true,
                    businessOnlyTransfer = true,
                    weeklyLeasePaymentCents = 7000,
                    leaseTermMonths = 12,
                    renewalSecurity01 = 0.2f,
                    sellerControlRights01 = 0.8f
                },
                delayedPossession = new DelayedPossessionTerms
                {
                    delayed = true,
                    minimumDelayDays = 14,
                    maximumDelayDays = 28,
                    source = DelayedPossessionSource.SellerRequested,
                    sellerContinuityNeed01 = 0.5f
                }
            }, CreateContext());

            CollectionAssert.DoesNotContain(result.ReasonCodes, ExpandedDealTermReasonCode.DocumentationWeak);
        }

        [Test]
        public void ExplicitWeakDocumentationOnComplexTerms_AddsDocumentationConcern()
        {
            ExpandedDealTermEvaluationResult result = new ExpandedDealTermEvaluator().Evaluate(new ExpandedDealTerms
            {
                retainedLandLease = new RetainedLandLeaseTerms
                {
                    retainedLandLease = true,
                    businessOnlyTransfer = true,
                    weeklyLeasePaymentCents = 7000,
                    leaseTermMonths = 12,
                    renewalSecurity01 = 0.2f,
                    sellerControlRights01 = 0.8f
                },
                documentationQuality01 = 0.2f
            }, CreateContext());

            CollectionAssert.Contains(result.ReasonCodes, ExpandedDealTermReasonCode.DocumentationWeak);
            Assert.AreEqual(ExpandedDealTermReasonCode.DocumentationWeak, result.PrimaryConcernCode);
            Assert.IsTrue(result.NeedsEnhancedDiligence);
        }

        [Test]
        public void BalloonSellerFinancingAddsRiskWithoutRemovingFinancingBenefit()
        {
            ExpandedDealTermEvaluator evaluator = new ExpandedDealTermEvaluator();
            ExpandedDealTermEvaluationContext context = CreateContext();

            ExpandedDealTermEvaluationResult plainOffered = evaluator.Evaluate(new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    requested = true,
                    offered = true,
                    principalCents = 300000,
                    downPaymentCents = 100000,
                    annualInterestRateBps = 700,
                    termMonths = 48,
                    sellerRiskTolerance01 = 0.75f
                },
                documentationQuality01 = 1f
            }, context);

            ExpandedDealTermEvaluationResult ballooned = evaluator.Evaluate(new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    requested = true,
                    offered = true,
                    principalCents = 300000,
                    downPaymentCents = 100000,
                    annualInterestRateBps = 700,
                    termMonths = 48,
                    balloonPaymentCents = 120000,
                    sellerRiskTolerance01 = 0.75f
                },
                documentationQuality01 = 1f
            }, context);

            CollectionAssert.Contains(ballooned.ReasonCodes, ExpandedDealTermReasonCode.BalloonPaymentRisk);
            Assert.Greater(ballooned.Risk01, plainOffered.Risk01);
            Assert.Greater(ballooned.BuyerBurden01, plainOffered.BuyerBurden01);
            Assert.Less(ballooned.ThirdPartyFinancingNeedCents, 400000);
            Assert.AreEqual(ExpandedDealReadinessClass.ReviewClosely, ballooned.ReadinessClass);
        }

        [Test]
        public void LongDelayedPossessionAddsSpecificReasonAndMoreBurdenThanShortDelay()
        {
            ExpandedDealTermEvaluator evaluator = new ExpandedDealTermEvaluator();
            ExpandedDealTermEvaluationContext context = CreateContext();

            ExpandedDealTermEvaluationResult shortDelay = evaluator.Evaluate(new ExpandedDealTerms
            {
                delayedPossession = new DelayedPossessionTerms
                {
                    delayed = true,
                    minimumDelayDays = 7,
                    maximumDelayDays = 21,
                    source = DelayedPossessionSource.SellerRequested,
                    sellerContinuityNeed01 = 0.5f
                },
                documentationQuality01 = 1f
            }, context);

            ExpandedDealTermEvaluationResult longDelay = evaluator.Evaluate(new ExpandedDealTerms
            {
                delayedPossession = new DelayedPossessionTerms
                {
                    delayed = true,
                    minimumDelayDays = 45,
                    maximumDelayDays = 90,
                    source = DelayedPossessionSource.SellerRequested,
                    sellerContinuityNeed01 = 0.5f
                },
                documentationQuality01 = 1f
            }, context);

            CollectionAssert.DoesNotContain(shortDelay.ReasonCodes, ExpandedDealTermReasonCode.LongPossessionDelay);
            CollectionAssert.Contains(longDelay.ReasonCodes, ExpandedDealTermReasonCode.LongPossessionDelay);
            Assert.Greater(longDelay.BuyerBurden01, shortDelay.BuyerBurden01);
            Assert.Greater(longDelay.Risk01, shortDelay.Risk01);
        }

        [Test]
        public void RetainedLandLeaseRiskShapingExposesShortLeaseRenewalAndControlReasons()
        {
            ExpandedDealTermEvaluationResult fragileLease = new ExpandedDealTermEvaluator().Evaluate(new ExpandedDealTerms
            {
                retainedLandLease = new RetainedLandLeaseTerms
                {
                    retainedLandLease = true,
                    businessOnlyTransfer = true,
                    weeklyLeasePaymentCents = 5000,
                    leaseTermMonths = 12,
                    renewalSecurity01 = 0.2f,
                    sellerControlRights01 = 0.8f
                },
                documentationQuality01 = 1f
            }, CreateContext());

            CollectionAssert.Contains(fragileLease.ReasonCodes, ExpandedDealTermReasonCode.ShortLeaseTerm);
            CollectionAssert.Contains(fragileLease.ReasonCodes, ExpandedDealTermReasonCode.WeakRenewalSecurity);
            CollectionAssert.Contains(fragileLease.ReasonCodes, ExpandedDealTermReasonCode.HeavySellerControl);
            Assert.AreEqual(ExpandedDealInspectionFocus.LandControl, fragileLease.PrimaryInspectionFocus);
        }

        [Test]
        public void StandardTermInspectionSummary_IsCompactAndStable()
        {
            ExpandedDealTerms terms = new ExpandedDealTerms
            {
                inventoryTransfer = new InventoryTransferTerms
                {
                    transferMode = InventoryTransferMode.Included,
                    estimatedInventoryValueCents = 50000,
                    operatingContinuity01 = 0.8f
                }
            }.Sanitized(400000);

            Assert.AreEqual("standard transfer", terms.StructureSummary);
            Assert.AreEqual("routine diligence", terms.ReviewSummary);
            StringAssert.Contains("Tags: standard transfer", terms.InspectionSummaryBlock);
        }

        [Test]
        public void EvaluationResult_ReadinessAndReviewSummaryStayActionable()
        {
            ExpandedDealTermEvaluationResult result = new ExpandedDealTermEvaluator().Evaluate(new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    requested = true,
                    offered = true,
                    principalCents = 320000,
                    downPaymentCents = 80000,
                    annualInterestRateBps = 650,
                    termMonths = 60,
                    balloonPaymentCents = 100000,
                    paymentCadence = SellerFinancingPaymentCadence.Monthly
                },
                documentationQuality01 = 0.45f
            }, CreateContext());

            Assert.AreEqual(ExpandedDealTermReasonCode.DocumentationWeak, result.PrimaryConcernCode);
            Assert.AreEqual(ExpandedDealReadinessClass.ReviewClosely, result.ReadinessClass);
            StringAssert.Contains("Headline:", result.InspectionSummaryBlock);
            StringAssert.Contains("Review first:", result.InspectionSummaryBlock);
            StringAssert.Contains("enhanced diligence", result.TagSummary);
        }

        [Test]
        public void AdapterSyncsLegacySellerFinancingFlagToExplicitExpandedTerms()
        {
            AcquisitionOfferTerms offerTerms = new AcquisitionOfferTerms
            {
                offerPriceCents = 500000,
                sellerFinancingRequested = true
            };

            AcquisitionOfferTerms updated = ExpandedDealTermAdapters.WithExpandedDealTerms(offerTerms, new ExpandedDealTerms
            {
                inventoryTransfer = new InventoryTransferTerms
                {
                    transferMode = InventoryTransferMode.Included,
                    estimatedInventoryValueCents = 80000,
                    operatingContinuity01 = 0.75f
                },
                documentationQuality01 = 1f
            });

            Assert.IsFalse(updated.sellerFinancingRequested);
            Assert.IsTrue(updated.expandedDealTerms.inventoryTransfer.HasTerms);
        }

        [Test]
        public void SellerFinancingAdapterBuildsLoanTermFromExpandedTerms()
        {
            ExpandedDealTerms terms = new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    offered = true,
                    principalCents = 320000,
                    downPaymentCents = 80000,
                    annualInterestRateBps = 650,
                    termMonths = 60,
                    paymentCadence = SellerFinancingPaymentCadence.Monthly
                },
                documentationQuality01 = 1f
            };

            bool built = ExpandedDealTermAdapters.TryBuildSellerFinancingLoanTerm(terms, 500000, out LoanTermStructure loanTerm);

            Assert.IsTrue(built);
            Assert.AreEqual(320000, loanTerm.principalCents);
            Assert.AreEqual(RepaymentFrequency.Monthly, loanTerm.repaymentFrequency);
        }

        [Test]
        public void EvaluationScoresStayBoundedWithExtremeTerms()
        {
            ExpandedDealTerms terms = new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    requested = true,
                    offered = true,
                    principalCents = 700000,
                    downPaymentCents = 10000,
                    balloonPaymentCents = 680000,
                    termMonths = 12,
                    sellerRiskTolerance01 = 0.1f
                },
                inventoryTransfer = new InventoryTransferTerms
                {
                    transferMode = InventoryTransferMode.Excluded,
                    estimatedInventoryValueCents = 99999999,
                    restockBurden01 = 1f
                },
                delayedPossession = new DelayedPossessionTerms
                {
                    delayed = true,
                    minimumDelayDays = 120,
                    maximumDelayDays = 365,
                    source = DelayedPossessionSource.BuyerRequested,
                    sellerContinuityNeed01 = 1f
                },
                retainedLandLease = new RetainedLandLeaseTerms
                {
                    retainedLandLease = true,
                    businessOnlyTransfer = true,
                    weeklyLeasePaymentCents = 40000,
                    leaseTermMonths = 6,
                    estimatedRetainedLandValueCents = 99999999,
                    renewalSecurity01 = 0f,
                    sellerControlRights01 = 1f
                },
                documentationQuality01 = 0.1f
            };

            ExpandedDealTermEvaluationResult result = new ExpandedDealTermEvaluator().Evaluate(terms, CreateContext());

            Assert.That(result.DealAttractiveness01, Is.InRange(0f, 1f));
            Assert.That(result.Risk01, Is.InRange(0f, 1f));
            Assert.That(result.BuyerBurden01, Is.InRange(0f, 1f));
            Assert.That(result.SellerWillingnessDelta, Is.InRange(-0.5f, 0.5f));
            Assert.GreaterOrEqual(result.ThirdPartyFinancingNeedCents, 0);
        }



        [Test]
        public void ComplexTermSanitization_DoesNotMutateCallerSuppliedAuthoringState()
        {
            ExpandedDealTerms original = new ExpandedDealTerms
            {
                inventoryTransfer = new InventoryTransferTerms
                {
                    transferMode = InventoryTransferMode.Partial,
                    estimatedInventoryValueCents = 60000
                },
                retainedLandLease = new RetainedLandLeaseTerms
                {
                    retainedLandLease = true,
                    businessOnlyTransfer = true,
                    weeklyLeasePaymentCents = 7000,
                    leaseTermMonths = 12,
                    renewalSecurity01 = 0.2f,
                    sellerControlRights01 = 0.8f
                },
                delayedPossession = new DelayedPossessionTerms
                {
                    delayed = true,
                    minimumDelayDays = 14,
                    maximumDelayDays = 28,
                    source = DelayedPossessionSource.SellerRequested,
                    sellerContinuityNeed01 = 0.5f
                }
            };

            ExpandedDealTerms sanitized = original.Sanitized(500000);

            Assert.AreEqual(0f, original.documentationQuality01, 0.0001f);
            Assert.AreEqual(0f, original.inventoryTransfer.operatingContinuity01, 0.0001f);
            Assert.AreEqual(0f, original.inventoryTransfer.restockBurden01, 0.0001f);
            Assert.AreEqual(0f, original.inventoryTransfer.verificationRisk01, 0.0001f);
            Assert.AreEqual(0.72f, sanitized.documentationQuality01, 0.0001f);
            Assert.AreEqual(0.35f, sanitized.inventoryTransfer.operatingContinuity01, 0.0001f);
            Assert.AreEqual(0.35f, sanitized.inventoryTransfer.restockBurden01, 0.0001f);
            Assert.AreEqual(0.2f, sanitized.inventoryTransfer.verificationRisk01, 0.0001f);
        }

        [Test]
        public void Evaluator_DoesNotMutateCallerSuppliedExpandedTermsWhenApplyingDefaults()
        {
            ExpandedDealTerms original = new ExpandedDealTerms
            {
                retainedLandLease = new RetainedLandLeaseTerms
                {
                    retainedLandLease = true,
                    businessOnlyTransfer = true,
                    weeklyLeasePaymentCents = 7000,
                    leaseTermMonths = 12,
                    renewalSecurity01 = 0.2f,
                    sellerControlRights01 = 0.8f
                },
                delayedPossession = new DelayedPossessionTerms
                {
                    delayed = true,
                    minimumDelayDays = 14,
                    maximumDelayDays = 28,
                    source = DelayedPossessionSource.SellerRequested,
                    sellerContinuityNeed01 = 0.5f
                }
            };

            ExpandedDealTermEvaluationResult result = new ExpandedDealTermEvaluator().Evaluate(original, CreateContext());

            Assert.AreEqual(0f, original.documentationQuality01, 0.0001f);
            Assert.AreEqual(0f, original.inventoryTransfer.operatingContinuity01, 0.0001f);
            CollectionAssert.DoesNotContain(result.ReasonCodes, ExpandedDealTermReasonCode.DocumentationWeak);
        }

        [Test]
        public void AdapterSync_DoesNotMutateOriginalOfferOrExpandedTerms()
        {
            AcquisitionOfferTerms baseline = CreateBaselineOffer();
            ExpandedDealTerms expandedTerms = new ExpandedDealTerms
            {
                inventoryTransfer = new InventoryTransferTerms
                {
                    transferMode = InventoryTransferMode.Included,
                    estimatedInventoryValueCents = 80000,
                    operatingContinuity01 = 0.75f
                },
                documentationQuality01 = 1f
            };

            AcquisitionOfferTerms updated = ExpandedDealTermAdapters.WithExpandedDealTerms(baseline, expandedTerms);

            Assert.IsFalse(baseline.expandedDealTerms.HasAnyTerms);
            Assert.IsTrue(baseline.sellerFinancingRequested);
            Assert.IsTrue(expandedTerms.inventoryTransfer.HasTerms);
            Assert.IsFalse(expandedTerms.sellerFinancing.offered);
            Assert.IsTrue(updated.expandedDealTerms.inventoryTransfer.HasTerms);
            Assert.IsFalse(updated.sellerFinancingRequested);
        }

        private static AcquisitionOfferTerms CreateBaselineOffer()
        {
            return new AcquisitionOfferTerms
            {
                offerPriceCents = 300000,
                earnestMoneyCents = 30000,
                closingSpeed01 = 0.8f,
                contingencyBurden01 = 0.08f,
                inspectionStrictness01 = 0.15f,
                sellerFinancingRequested = true,
                nonPriceConcessions01 = 0.25f
            };
        }

        private static ExpandedDealTermEvaluationContext CreateContext(int priceCents = 500000, int availableCashCents = 100000)
        {
            return new ExpandedDealTermEvaluationContext
            {
                purchasePriceCents = priceCents,
                estimatedAssetValueCents = priceCents,
                buyerAvailableCashCents = availableCashCents,
                sellerPressure01 = 0.75f,
                sellerCashNeed01 = 0.8f,
                sellerAttachment01 = 0.7f,
                sellerRelationshipSensitivity01 = 0.65f,
                buyerReputation01 = 0.7f,
                buyerCashCertainty01 = 0.55f
            };
        }
    }
}
