using System;
using LandLedgers.Economy;
using LandLedgers.Economy.Valuation;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Valuation
{
    public sealed class ValuationFoundationTests
    {
        [Test]
        public void ValuationRisesWithStrongerLocationTrafficFrontageAndReadiness()
        {
            ParcelValuationEvaluator evaluator = new ParcelValuationEvaluator();
            ParcelValuationResult weak = evaluator.Evaluate(CreateWeakParcel());
            ParcelValuationResult strong = evaluator.Evaluate(CreateStrongParcel());

            Assert.Greater(strong.EstimatedValueCents, weak.EstimatedValueCents);
            Assert.Greater(strong.LocationScore01, weak.LocationScore01);
            Assert.Greater(strong.TrafficScore01, weak.TrafficScore01);
            Assert.Greater(strong.DevelopmentReadinessScore01, weak.DevelopmentReadinessScore01);
            Assert.IsTrue(strong.Scorecard.TryGetRow(ValuationScorecardCategory.CommercialPotential, out ScorecardRow commercialRow));
            Assert.Greater(commercialRow.Score100, 60);
        }

        [Test]
        public void SellerPressureChangesThresholdWithoutChangingIntrinsicParcelValue()
        {
            ParcelValuationResult valuation = new ParcelValuationEvaluator().Evaluate(CreateStrongParcel());
            SellerProfile distressed = CreateDistressedSeller();
            SellerProfile holdout = CreateAttachedHoldoutSeller();
            SellerPressureEvaluator pressureEvaluator = new SellerPressureEvaluator();
            SellerPressureResult distressedPressure = pressureEvaluator.Evaluate(distressed);
            SellerPressureResult holdoutPressure = pressureEvaluator.Evaluate(holdout);
            BuyerSellerFitResult buyerFit = new BuyerSellerFitEvaluator().Evaluate(CreateTrustedLocalBuyer(), distressed, distressedPressure);
            OfferEvaluationEngine engine = new OfferEvaluationEngine();

            OfferEvaluationResult distressedResult = engine.Evaluate(valuation, distressed, distressedPressure, buyerFit, CreateCleanOffer(valuation.EstimatedValueCents));
            OfferEvaluationResult holdoutResult = engine.Evaluate(valuation, holdout, holdoutPressure, buyerFit, CreateCleanOffer(valuation.EstimatedValueCents));

            Assert.AreEqual(valuation.EstimatedValueCents, valuation.EstimatedValueCents);
            Assert.Less(distressedResult.PressureAdjustedThresholdCents, holdoutResult.PressureAdjustedThresholdCents);
        }

        [Test]
        public void DistressedSellersTolerateLowerCleanOffersThanAttachedHoldouts()
        {
            ParcelValuationResult valuation = new ParcelValuationEvaluator().Evaluate(CreateStrongParcel());
            OfferEvaluationEngine engine = new OfferEvaluationEngine();
            BuyerOfferProfile buyer = CreateTrustedLocalBuyer();
            AcquisitionOfferTerms cleanAtValue = CreateCleanOffer(valuation.EstimatedValueCents);

            OfferEvaluationResult distressed = engine.Evaluate(valuation, CreateDistressedSeller(), buyer, cleanAtValue);
            OfferEvaluationResult attached = engine.Evaluate(valuation, CreateAttachedHoldoutSeller(), buyer, cleanAtValue);

            Assert.AreEqual(OfferDecision.Accept, distressed.Decision);
            Assert.AreEqual(OfferDecision.Reject, attached.Decision);
            CollectionAssert.Contains(attached.ReasonCodes, OfferEvaluationReasonCode.SellerAttached);
        }

        [Test]
        public void TrustedLocalBuyersScoreBetterThanThreateningRivalsForRelationshipSensitiveSellers()
        {
            SellerProfile seller = CreateAttachedHoldoutSeller();
            SellerPressureEvaluator pressureEvaluator = new SellerPressureEvaluator();
            SellerPressureResult pressure = pressureEvaluator.Evaluate(seller);
            BuyerSellerFitEvaluator fitEvaluator = new BuyerSellerFitEvaluator();

            BuyerSellerFitResult trusted = fitEvaluator.Evaluate(CreateTrustedLocalBuyer(), seller, pressure);
            BuyerSellerFitResult rival = fitEvaluator.Evaluate(CreateThreateningRivalBuyer(), seller, pressure);

            Assert.Greater(trusted.Score01, rival.Score01);
            CollectionAssert.Contains(trusted.ReasonCodes, OfferEvaluationReasonCode.TrustedBuyer);
            CollectionAssert.Contains(rival.ReasonCodes, OfferEvaluationReasonCode.BuyerDistrusted);
        }

        [Test]
        public void OfferEvaluationReturnsAcceptCounterRejectAndGroundedReasonCodes()
        {
            ParcelValuationResult valuation = new ParcelValuationEvaluator().Evaluate(CreateStrongParcel());
            SellerProfile seller = CreateDistressedSeller();
            SellerPressureResult pressure = new SellerPressureEvaluator().Evaluate(seller);
            BuyerSellerFitResult fit = new BuyerSellerFitEvaluator().Evaluate(CreateTrustedLocalBuyer(), seller, pressure);
            OfferEvaluationEngine engine = new OfferEvaluationEngine();
            int threshold = Mathf.RoundToInt(valuation.EstimatedValueCents * seller.minimumAcceptableRatio * pressure.ReservationValueMultiplier);

            OfferEvaluationResult accept = engine.Evaluate(valuation, seller, pressure, fit, CreateCleanOffer(threshold + 50));
            OfferEvaluationResult counter = engine.Evaluate(valuation, seller, pressure, fit, CreateCleanOffer(Mathf.RoundToInt(threshold * 0.84f)));
            OfferEvaluationResult reject = engine.Evaluate(valuation, CreateAttachedHoldoutSeller(), CreateThreateningRivalBuyer(), CreateRiskyOffer(Mathf.RoundToInt(threshold * 0.65f)));

            Assert.AreEqual(OfferDecision.Accept, accept.Decision);
            CollectionAssert.Contains(accept.ReasonCodes, OfferEvaluationReasonCode.PriceMeetsThreshold);
            Assert.AreEqual(OfferDecision.Counter, counter.Decision);
            CollectionAssert.Contains(counter.ReasonCodes, OfferEvaluationReasonCode.CounterPriceRecommended);
            Assert.AreEqual(OfferDecision.Reject, reject.Decision);
            CollectionAssert.Contains(reject.ReasonCodes, OfferEvaluationReasonCode.TooLow);
            CollectionAssert.Contains(reject.ReasonCodes, OfferEvaluationReasonCode.BuyerDistrusted);
        }

        [Test]
        public void OfferEvaluationDoesNotConsultFinancing()
        {
            ParcelValuationResult valuation = new ParcelValuationEvaluator().Evaluate(CreateStrongParcel());
            SellerProfile seller = CreateDistressedSeller();
            OfferEvaluationResult result = new OfferEvaluationEngine().Evaluate(
                valuation,
                seller,
                CreateTrustedLocalBuyer(),
                CreateCleanOffer(valuation.EstimatedValueCents * 2));

            Assert.AreEqual(OfferDecision.Accept, result.Decision);
            CollectionAssert.DoesNotContain(result.ReasonCodes, OfferEvaluationReasonCode.FinancingContingencyConcern);
        }

        [Test]
        public void LandAppreciationRisesOverTimeFromPurchaseBasis()
        {
            LandAppreciationEvaluator evaluator = new LandAppreciationEvaluator();
            LandAppreciationInputs inputs = CreateAppreciationInputs(0, 0, LandAppreciationImprovementState.Empty, 0.65f, 0.7f);
            LandAppreciationResult purchaseDay = evaluator.Evaluate(inputs);
            inputs.currentDayIndex = 365;
            LandAppreciationResult oneYear = evaluator.Evaluate(inputs);

            Assert.AreEqual(inputs.TotalBasisCents, purchaseDay.EstimatedValueCents);
            Assert.Greater(oneYear.EstimatedValueCents, purchaseDay.EstimatedValueCents);
            Assert.Greater(oneYear.AppreciationDeltaCents, 0);
        }

        [Test]
        public void LandAppreciationStaysInsideGroundedBounds()
        {
            LandAppreciationEvaluator evaluator = new LandAppreciationEvaluator();
            LandAppreciationInputs inputs = CreateAppreciationInputs(
                0,
                365 * 100,
                LandAppreciationImprovementState.OperatingBusiness,
                10f,
                10f);
            LandAppreciationResult result = evaluator.Evaluate(inputs);
            int totalBasis = inputs.TotalBasisCents;
            LandAppreciationWeights weights = LandAppreciationWeights.Default;

            Assert.GreaterOrEqual(result.EstimatedValueCents, Mathf.RoundToInt(totalBasis * weights.minEstimatedValueRatio));
            Assert.LessOrEqual(result.EstimatedValueCents, Mathf.RoundToInt(totalBasis * weights.maxEstimatedValueRatio));
        }

        [Test]
        public void LandAppreciationRewardsImprovementAndReadiness()
        {
            LandAppreciationEvaluator evaluator = new LandAppreciationEvaluator();
            LandAppreciationInputs emptyLowReadiness = CreateAppreciationInputs(
                0,
                365,
                LandAppreciationImprovementState.Empty,
                0.35f,
                0.2f);
            LandAppreciationInputs improvedReady = CreateAppreciationInputs(
                0,
                365,
                LandAppreciationImprovementState.ImprovedShell,
                0.35f,
                0.9f);

            LandAppreciationResult empty = evaluator.Evaluate(emptyLowReadiness);
            LandAppreciationResult improved = evaluator.Evaluate(improvedReady);

            Assert.Greater(improved.EstimatedValueCents, empty.EstimatedValueCents);
        }

        [Test]
        public void LandAppreciationPersistenceRoundTripsThroughAcquisitionSaveDto()
        {
            AcquisitionSaveDto dto = new AcquisitionSaveDto
            {
                marketSeed = 1902,
                maxLandListings = 4,
                maxBusinessListings = 2
            };
            dto.ownedPlotIds.Add(7);
            dto.landAppreciations.Add(new LandAppreciationSaveDto
            {
                plotId = 7,
                buildingId = 3,
                holdingKind = LandAppreciationHoldingKind.UrbanParcel,
                improvementState = LandAppreciationImprovementState.ImprovedShell,
                purchaseBasisCents = 12000,
                capitalizedImprovementCents = 4000,
                currentEstimatedValueCents = 17000,
                appreciationDeltaCents = 1000,
                purchaseDayIndex = 12,
                lastValuationDayIndex = 42,
                townGrowthPressure01 = 0.7f,
                developmentReadiness01 = 0.8f
            });

            string json = JsonUtility.ToJson(dto);
            AcquisitionSaveDto restored = JsonUtility.FromJson<AcquisitionSaveDto>(json);

            Assert.NotNull(restored);
            Assert.AreEqual(1, restored.landAppreciations.Count);
            Assert.AreEqual(7, restored.landAppreciations[0].plotId);
            Assert.AreEqual(12000, restored.landAppreciations[0].purchaseBasisCents);
            Assert.AreEqual(4000, restored.landAppreciations[0].capitalizedImprovementCents);
            Assert.AreEqual(LandAppreciationImprovementState.ImprovedShell, restored.landAppreciations[0].improvementState);
        }

        [Test]
        public void OldAcquisitionSavesBackfillOwnedPlotAppreciation()
        {
            using AppreciationTestWorld context = AppreciationTestWorld.Create();
            int plotId = context.FindEmptyPlotId();
            AcquisitionSaveDto oldStyleDto = new AcquisitionSaveDto
            {
                marketSeed = 1902,
                maxLandListings = 4,
                maxBusinessListings = 2
            };
            oldStyleDto.ownedPlotIds.Add(plotId);

            context.AcquisitionMarket.LoadFromSaveDto(oldStyleDto);

            Assert.IsTrue(context.AcquisitionMarket.TryGetLandAppreciationForPlot(plotId, out LandAppreciationState appreciation));
            Assert.NotNull(appreciation);
            Assert.AreEqual(plotId, appreciation.plotId);
            Assert.Greater(appreciation.purchaseBasisCents, 0);
            Assert.AreEqual(appreciation.TotalBasisCents, appreciation.currentEstimatedValueCents);
            Assert.AreEqual(0, appreciation.appreciationDeltaCents);
        }

        [Test]
        public void MixedUseFriendlyResidentialParcelStillRetainsCommercialPotential()
        {
            ParcelValuationInputs mixedUseResidential = CreateStrongParcel();
            mixedUseResidential.assetId = "mixed_use_residential";
            mixedUseResidential.zone = PlotZone.Residential;
            mixedUseResidential.trafficExposure01 = 0.82f;
            mixedUseResidential.nearbyBusinesses01 = 0.55f;
            mixedUseResidential.nearbyHouseholds01 = 0.8f;
            mixedUseResidential.commercialFit01 = 0.78f;
            mixedUseResidential.developmentReadiness01 = 0.72f;

            ParcelValuationResult result = new ParcelValuationEvaluator().Evaluate(mixedUseResidential);
            ParcelValuationResult weak = new ParcelValuationEvaluator().Evaluate(CreateWeakParcel());

            Assert.IsTrue(result.Scorecard.TryGetRow(ValuationScorecardCategory.CommercialPotential, out ScorecardRow commercialRow));
            Assert.Greater(commercialRow.Score100, 35);
            Assert.Greater(result.EstimatedValueCents, weak.EstimatedValueCents);
        }

        private static LandAppreciationInputs CreateAppreciationInputs(
            int purchaseDayIndex,
            int currentDayIndex,
            LandAppreciationImprovementState improvementState,
            float townGrowthPressure01,
            float developmentReadiness01)
        {
            return new LandAppreciationInputs
            {
                holdingKind = LandAppreciationHoldingKind.UrbanParcel,
                improvementState = improvementState,
                purchaseBasisCents = 100000,
                capitalizedImprovementCents = 25000,
                purchaseDayIndex = purchaseDayIndex,
                currentDayIndex = currentDayIndex,
                townGrowthPressure01 = townGrowthPressure01,
                developmentReadiness01 = developmentReadiness01
            };
        }

        private sealed class AppreciationTestWorld : IDisposable
        {
            private readonly GameObject root;
            private readonly TownGenerationSettings settings;

            public TownWorldController TownWorld { get; }
            public AcquisitionMarketManager AcquisitionMarket { get; }

            private AppreciationTestWorld(GameObject root, TownGenerationSettings settings)
            {
                this.root = root;
                this.settings = settings;
                TownWorld = root.AddComponent<TownWorldController>();
                AcquisitionMarket = root.AddComponent<AcquisitionMarketManager>();
            }

            public static AppreciationTestWorld Create()
            {
                TownGenerationSettings sourceSettings = AssetDatabase.LoadAssetAtPath<TownGenerationSettings>("Assets/Core/World/DefaultTownGenerationSettings.asset");
                Assert.NotNull(sourceSettings);

                TownGenerationSettings settings = UnityEngine.Object.Instantiate(sourceSettings);
                GameObject root = new GameObject("Land Appreciation Test World");
                AppreciationTestWorld context = new AppreciationTestWorld(root, settings);
                context.TownWorld.Configure(settings, null, null);
                context.TownWorld.GenerateTownShell();
                context.AcquisitionMarket.Configure(context.TownWorld, null);
                context.AcquisitionMarket.RebuildMarket();
                return context;
            }

            public int FindEmptyPlotId()
            {
                for (int i = 0; i < TownWorld.Plots.Count; i++)
                {
                    TownPlot plot = TownWorld.Plots[i];
                    if (plot != null && plot.buildingId < 0)
                    {
                        return plot.id;
                    }
                }

                Assert.Fail("No empty plot found in the generated test town.");
                return -1;
            }

            public void Dispose()
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (settings != null)
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
            }
        }

        private static ParcelValuationInputs CreateStrongParcel()
        {
            return new ParcelValuationInputs
            {
                assetId = "strong_parcel",
                plotId = 1,
                buildingId = -1,
                assetKind = ParcelAssetKind.Land,
                zone = PlotZone.Business,
                siteSizeCells = new Vector2Int(8, 8),
                buildableAreaCells = 60,
                frontageCells = 8,
                depthCells = 8,
                roadFrontageDirection = GridDirection.North,
                distanceToMainStreet01 = 0.05f,
                distanceToTownCenter01 = 0.1f,
                trafficExposure01 = 0.9f,
                nearbyHouseholds01 = 0.75f,
                nearbyBusinesses01 = 0.8f,
                competitionPressure01 = 0.25f,
                commercialFit01 = 0.92f,
                developmentReadiness01 = 0.9f,
                improvementCondition01 = 1f,
                knownConstraints01 = 0.05f,
                listedForSale = true
            };
        }

        private static ParcelValuationInputs CreateWeakParcel()
        {
            return new ParcelValuationInputs
            {
                assetId = "weak_parcel",
                plotId = 2,
                buildingId = -1,
                assetKind = ParcelAssetKind.Land,
                zone = PlotZone.Residential,
                siteSizeCells = new Vector2Int(5, 5),
                buildableAreaCells = 12,
                frontageCells = 2,
                depthCells = 5,
                roadFrontageDirection = GridDirection.South,
                distanceToMainStreet01 = 0.9f,
                distanceToTownCenter01 = 0.85f,
                trafficExposure01 = 0.2f,
                nearbyHouseholds01 = 0.35f,
                nearbyBusinesses01 = 0.1f,
                competitionPressure01 = 0.65f,
                commercialFit01 = 0.25f,
                developmentReadiness01 = 0.25f,
                improvementCondition01 = 1f,
                knownConstraints01 = 0.55f,
                listedForSale = true
            };
        }

        private static SellerProfile CreateDistressedSeller()
        {
            return new SellerProfile
            {
                sellerId = "distressed",
                motive = SellerMotive.FinancialDistress,
                pressure01 = 0.9f,
                urgency01 = 0.85f,
                debtPressure01 = 0.9f,
                cashNeed01 = 0.85f,
                attachment01 = 0.05f,
                relocationNeed01 = 0.25f,
                reputationSensitivity01 = 0.15f,
                buyerPreferenceSensitivity01 = 0.15f,
                listedForSale = true,
                minimumAcceptableRatio = 1f,
                counterofferFlexibility01 = 0.8f
            };
        }

        private static SellerProfile CreateAttachedHoldoutSeller()
        {
            return new SellerProfile
            {
                sellerId = "attached",
                motive = SellerMotive.OwnerOperatorAttachment,
                pressure01 = 0.05f,
                urgency01 = 0.05f,
                debtPressure01 = 0f,
                cashNeed01 = 0.05f,
                attachment01 = 0.95f,
                relocationNeed01 = 0f,
                reputationSensitivity01 = 0.9f,
                buyerPreferenceSensitivity01 = 0.95f,
                listedForSale = false,
                minimumAcceptableRatio = 1f,
                counterofferFlexibility01 = 0.15f
            };
        }

        private static BuyerOfferProfile CreateTrustedLocalBuyer()
        {
            return new BuyerOfferProfile
            {
                buyerId = "trusted_local",
                buyerKind = BuyerKind.LocalOperator,
                reputation01 = 0.9f,
                localTrust01 = 0.92f,
                priorDealReliability01 = 0.85f,
                strategicThreat01 = 0.05f,
                communityFit01 = 0.9f,
                cashCertainty01 = 0.85f,
                relationshipWithSeller01 = 0.8f
            };
        }

        private static BuyerOfferProfile CreateThreateningRivalBuyer()
        {
            return new BuyerOfferProfile
            {
                buyerId = "rival",
                buyerKind = BuyerKind.Rival,
                reputation01 = 0.25f,
                localTrust01 = 0.1f,
                priorDealReliability01 = 0.3f,
                strategicThreat01 = 0.95f,
                communityFit01 = 0.1f,
                cashCertainty01 = 0.8f,
                relationshipWithSeller01 = 0f
            };
        }

        private static AcquisitionOfferTerms CreateCleanOffer(int priceCents)
        {
            return new AcquisitionOfferTerms
            {
                offerPriceCents = priceCents,
                earnestMoneyCents = Mathf.RoundToInt(priceCents * 0.1f),
                closingSpeed01 = 0.9f,
                contingencyBurden01 = 0.05f,
                inspectionStrictness01 = 0.15f,
                sellerFinancingRequested = false,
                nonPriceConcessions01 = 0.35f
            };
        }

        private static AcquisitionOfferTerms CreateRiskyOffer(int priceCents)
        {
            return new AcquisitionOfferTerms
            {
                offerPriceCents = priceCents,
                earnestMoneyCents = 0,
                closingSpeed01 = 0.15f,
                contingencyBurden01 = 0.9f,
                inspectionStrictness01 = 0.8f,
                sellerFinancingRequested = true,
                nonPriceConcessions01 = 0f
            };
        }
    }
}
