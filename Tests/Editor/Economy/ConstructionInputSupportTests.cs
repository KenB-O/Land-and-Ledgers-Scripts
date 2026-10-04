using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class ConstructionInputSupportTests
    {
        private const int TestOwnerConstructionCashCents = 2_000_000;

        [Test]
        public void ShellQuoteIncludesPositiveConstructionInputs()
        {
            using TestWorld context = TestWorld.Create();

            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out ConstructionInputQuote quote));

            Assert.Greater(quote.CashCostCents, 0);
            Assert.Greater(quote.GetLine(ConstructionResourceKind.Lumber).requiredUnits, 0);
            Assert.Greater(quote.GetLine(ConstructionResourceKind.Nails).requiredUnits, 0);
            Assert.Greater(quote.GetLine(ConstructionResourceKind.Labor).requiredUnits, 0);
        }

        [Test]
        public void ShellConstructionBlocksWhenLocalLumberMarketIsMissing()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out ConstructionInputQuote quote));
            context.SetLumberYardStock(0);
            context.SetSawmillStock(Mathf.Max(0, quote.GetLine(ConstructionResourceKind.Lumber).requiredUnits - 1));

            bool built = context.AcquisitionMarket.TryConstructOnOwnedPlot(plotId, optionIndex, out _, out string message);

            Assert.IsFalse(built);
            StringAssert.Contains("Lumber", message);
        }

        [Test]
        public void ShellConstructionUsesPaidRegionalHardwareWhenBlacksmithHardwareIsMissing()
        {
            using TestWorld context = TestWorld.Create();
            context.SetBlacksmithHardwareStock(0);
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out ConstructionInputQuote quote));
            ConstructionResourceQuoteLine hardware = quote.GetLine(ConstructionResourceKind.Nails);
            Assert.NotNull(hardware);
            StringAssert.Contains("Regional freight hardware", hardware.sourceLabel);
            StringAssert.Contains("1 week freight lead", hardware.procurementNote);
            Assert.Greater(hardware.unitCostCents, 250);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;
            BusinessInstanceState blacksmith = context.GetBusiness(BusinessType.Blacksmith);
            int blacksmithCashBefore = blacksmith.RuntimeState.CurrentCashCents;

            bool built = context.AcquisitionMarket.TryConstructOnOwnedPlot(plotId, optionIndex, out _, out string message);

            Assert.IsTrue(built, message);
            Assert.AreEqual(0, context.GetBlacksmithHardwareStock());
            Assert.AreEqual(ownerCashBefore - quote.CashCostCents, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(blacksmithCashBefore, blacksmith.RuntimeState.CurrentCashCents);
        }

        [Test]
        public void SuccessfulShellConstructionConsumesSawmillAndBlacksmithInputs()
        {
            using TestWorld context = TestWorld.Create();
            context.SetLumberYardStock(0);
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out ConstructionInputQuote quote));
            int lumberBefore = context.GetSawmillStock();
            int hardwareBefore = context.GetBlacksmithHardwareStock();
            BusinessInstanceState blacksmith = context.GetBusiness(BusinessType.Blacksmith);
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            int blacksmithCashBefore = blacksmith.RuntimeState.CurrentCashCents;
            int sawmillCashBefore = sawmill.RuntimeState.CurrentCashCents;

            bool built = context.AcquisitionMarket.TryConstructOnOwnedPlot(plotId, optionIndex, out PlacedBuilding building, out string message);

            Assert.IsTrue(built, message);
            Assert.NotNull(building);
            Assert.AreEqual(lumberBefore - quote.GetLine(ConstructionResourceKind.Lumber).requiredUnits, context.GetSawmillStock());
            Assert.AreEqual(hardwareBefore - quote.GetLine(ConstructionResourceKind.Nails).requiredUnits, context.GetBlacksmithHardwareStock());
            Assert.Greater(blacksmith.RuntimeState.LastWeeklyLocalTransferRevenueCents, 0);
            Assert.Greater(blacksmith.RuntimeState.CurrentCashCents, blacksmithCashBefore);
            Assert.Greater(sawmill.RuntimeState.LastWeeklyLocalTransferRevenueCents, 0);
            Assert.Greater(sawmill.RuntimeState.CurrentCashCents, sawmillCashBefore);
        }

        [Test]
        public void RegionalHardwareQuoteCostsMoreThanLocalBlacksmithQuote()
        {
            using TestWorld localContext = TestWorld.Create();
            localContext.SetBlacksmithHardwareStock(1000);
            Assert.IsTrue(localContext.TryFindBuildableOption(out _, out _, out ConstructionInputQuote localQuote));
            ConstructionResourceQuoteLine localHardware = localQuote.GetLine(ConstructionResourceKind.Nails);
            Assert.NotNull(localHardware);

            using TestWorld fallbackContext = TestWorld.Create();
            fallbackContext.SetBlacksmithHardwareStock(0);
            Assert.IsTrue(fallbackContext.TryFindBuildableOption(out _, out _, out ConstructionInputQuote fallbackQuote));
            ConstructionResourceQuoteLine fallbackHardware = fallbackQuote.GetLine(ConstructionResourceKind.Nails);
            Assert.NotNull(fallbackHardware);

            Assert.Greater(fallbackHardware.unitCostCents, localHardware.unitCostCents);
            Assert.Greater(fallbackQuote.CashCostCents, localQuote.CashCostCents);
            StringAssert.Contains("Regional freight hardware", fallbackHardware.sourceLabel);
        }

        [Test]
        public void BusinessActivationFitOutConsumesInputsAndKeepsStartupCost()
        {
            using TestWorld context = TestWorld.Create();
            context.SetLumberYardStock(0);
            Assert.IsTrue(context.TryBuildShell(out PlacedBuilding shell));
            Assert.IsTrue(context.TryFindActivationOption(shell.id, out int optionIndex, out BusinessActivationCandidateState candidate, out ConstructionInputQuote quote));
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;
            int storeCashBefore = context.StoreRuntime.CurrentCashCents;
            int lumberBefore = context.GetSawmillStock();
            int hardwareBefore = context.GetBlacksmithHardwareStock();

            bool started = context.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, optionIndex, out BusinessInstanceState business, out string message);

            Assert.IsTrue(started, message);
            Assert.NotNull(business);
            Assert.AreEqual(candidate.startupCostCents, quote.CashCostCents);
            Assert.AreEqual(ownerCashBefore - quote.CashCostCents, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(storeCashBefore, context.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(lumberBefore - quote.GetLine(ConstructionResourceKind.Lumber).requiredUnits, context.GetSawmillStock());
            Assert.AreEqual(hardwareBefore - quote.GetLine(ConstructionResourceKind.Nails).requiredUnits, context.GetBlacksmithHardwareStock());
        }

        [Test]
        public void LateShellConstructionFailureRollsBackOwnerCashAndSupplierInputs()
        {
            using TestWorld context = TestWorld.Create();
            context.SetLumberYardStock(0);
            context.SetBlacksmithHardwareStock(1000);
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out _));
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            BusinessInstanceState blacksmith = context.GetBusiness(BusinessType.Blacksmith);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;
            int sawmillStockBefore = context.GetSawmillStock();
            int blacksmithStockBefore = context.GetBlacksmithHardwareStock();
            int sawmillCashBefore = sawmill.RuntimeState.CurrentCashCents;
            int blacksmithCashBefore = blacksmith.RuntimeState.CurrentCashCents;
            int sawmillRevenueBefore = sawmill.RuntimeState.LastWeeklyLocalTransferRevenueCents;
            int blacksmithRevenueBefore = blacksmith.RuntimeState.LastWeeklyLocalTransferRevenueCents;

            context.AcquisitionMarket.ForceLateConstructionFailureForTests = true;
            bool built = context.AcquisitionMarket.TryConstructOnOwnedPlot(plotId, optionIndex, out _, out string message);

            Assert.IsFalse(built);
            StringAssert.Contains("Forced late construction failure", message);
            Assert.AreEqual(ownerCashBefore, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(sawmillStockBefore, context.GetSawmillStock());
            Assert.AreEqual(blacksmithStockBefore, context.GetBlacksmithHardwareStock());
            Assert.AreEqual(sawmillCashBefore, sawmill.RuntimeState.CurrentCashCents);
            Assert.AreEqual(blacksmithCashBefore, blacksmith.RuntimeState.CurrentCashCents);
            Assert.AreEqual(sawmillRevenueBefore, sawmill.RuntimeState.LastWeeklyLocalTransferRevenueCents);
            Assert.AreEqual(blacksmithRevenueBefore, blacksmith.RuntimeState.LastWeeklyLocalTransferRevenueCents);
        }

        [Test]
        public void LateBusinessActivationFailureRollsBackFitOutCashAndSupplierInputs()
        {
            using TestWorld context = TestWorld.Create();
            context.SetLumberYardStock(0);
            context.SetBlacksmithHardwareStock(1000);
            Assert.IsTrue(context.TryBuildShell(out PlacedBuilding shell));
            Assert.IsTrue(context.TryFindActivationOption(shell.id, out int optionIndex, out _, out _));
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            BusinessInstanceState blacksmith = context.GetBusiness(BusinessType.Blacksmith);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;
            int sawmillStockBefore = context.GetSawmillStock();
            int blacksmithStockBefore = context.GetBlacksmithHardwareStock();
            int sawmillCashBefore = sawmill.RuntimeState.CurrentCashCents;
            int blacksmithCashBefore = blacksmith.RuntimeState.CurrentCashCents;
            int sawmillRevenueBefore = sawmill.RuntimeState.LastWeeklyLocalTransferRevenueCents;
            int blacksmithRevenueBefore = blacksmith.RuntimeState.LastWeeklyLocalTransferRevenueCents;

            context.AcquisitionMarket.ForceLateBusinessStartFailureForTests = true;
            bool started = context.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, optionIndex, out _, out string message);

            Assert.IsFalse(started);
            StringAssert.Contains("Forced late business start failure", message);
            Assert.AreEqual(ownerCashBefore, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(sawmillStockBefore, context.GetSawmillStock());
            Assert.AreEqual(blacksmithStockBefore, context.GetBlacksmithHardwareStock());
            Assert.AreEqual(sawmillCashBefore, sawmill.RuntimeState.CurrentCashCents);
            Assert.AreEqual(blacksmithCashBefore, blacksmith.RuntimeState.CurrentCashCents);
            Assert.AreEqual(sawmillRevenueBefore, sawmill.RuntimeState.LastWeeklyLocalTransferRevenueCents);
            Assert.AreEqual(blacksmithRevenueBefore, blacksmith.RuntimeState.LastWeeklyLocalTransferRevenueCents);
            Assert.AreEqual(0, context.AcquisitionMarket.OwnedBusinessCount);
        }

        [Test]
        public void PlayerStartedBlacksmithOpensWithActiveRequiredStaff()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(TryConstructShellForBusiness(context, BusinessType.Blacksmith, out PlacedBuilding shell, out string buildMessage), buildMessage);
            Assert.IsTrue(TryFindActivationOption(context, shell.id, BusinessType.Blacksmith, out int optionIndex, out _, out _));

            bool started = context.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, optionIndex, out BusinessInstanceState business, out string message);

            Assert.IsTrue(started, message);
            Assert.NotNull(business);
            Assert.AreEqual(BusinessType.Blacksmith, business.BusinessType);
            Assert.GreaterOrEqual(business.RuntimeState.ActiveRequiredWorkerCount, business.RuntimeState.RequiredWorkerCount);
            Assert.GreaterOrEqual(business.RuntimeState.OperatingEfficiency01, 0.75f);
            StringAssert.Contains("Assigned", message);
        }

        [Test]
        public void PlayerStartedRanchOpensWithActiveRequiredStaff()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(TryConstructShellForBusiness(context, BusinessType.Ranch, out PlacedBuilding shell, out string buildMessage), buildMessage);
            Assert.IsTrue(TryFindActivationOption(context, shell.id, BusinessType.Ranch, out int optionIndex, out _, out _));

            bool started = context.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, optionIndex, out BusinessInstanceState business, out string message);

            Assert.IsTrue(started, message);
            Assert.NotNull(business);
            Assert.AreEqual(BusinessType.Ranch, business.BusinessType);
            Assert.GreaterOrEqual(business.RuntimeState.ActiveRequiredWorkerCount, business.RuntimeState.RequiredWorkerCount);
            Assert.GreaterOrEqual(business.RuntimeState.OperatingEfficiency01, 0.75f);
        }

        [Test]
        public void PlayerStartedMineOpensWithMineStateAndOwnedBusinessCount()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(TryConstructShellForBusiness(context, BusinessType.Mine, out PlacedBuilding shell, out string buildMessage), buildMessage);
            Assert.IsTrue(TryFindActivationOption(context, shell.id, BusinessType.Mine, out int optionIndex, out _, out _));

            bool started = context.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, optionIndex, out BusinessInstanceState business, out string message);

            Assert.IsTrue(started, message);
            Assert.NotNull(business);
            Assert.AreEqual(BusinessType.Mine, business.BusinessType);
            Assert.AreEqual(1, context.AcquisitionMarket.OwnedBusinessCount);
            Assert.NotNull(business.MineState);
            Assert.IsFalse(string.IsNullOrWhiteSpace(business.MineState.StockpileCategoryId));
            Assert.GreaterOrEqual(business.RuntimeState.ActiveRequiredWorkerCount, business.RuntimeState.RequiredWorkerCount);
        }

        [Test]
        public void PlayerBusinessActivationBlocksWhenNoRequiredWorkerIsAvailable()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(TryConstructShellForBusiness(context, BusinessType.Blacksmith, out PlacedBuilding shell, out string buildMessage), buildMessage);
            context.PopulationManager.State.people.Clear();

            bool started = context.SharedBusinessRuntime.TryStartPlayerBusinessAtShell(BusinessType.Blacksmith, shell.id, out BusinessInstanceState business, out string message);

            Assert.IsFalse(started);
            Assert.IsNull(business);
            Assert.IsNull(context.SharedBusinessRuntime.FindByBuildingId(shell.id));
            StringAssert.Contains("cannot open until a required worker", message);
        }

        [Test]
        public void FirstEligibleActivationCandidateSkipsBlockedGeneralStore()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryBuildShell(out PlacedBuilding shell));

            int firstEligibleIndex = context.AcquisitionMarket.GetFirstEligibleBusinessActivationCandidateIndex(shell.id);

            Assert.GreaterOrEqual(firstEligibleIndex, 0);
            Assert.IsTrue(context.AcquisitionMarket.TryGetBusinessActivationCandidate(shell.id, firstEligibleIndex, out BusinessActivationCandidateState candidate));
            Assert.NotNull(candidate);
            Assert.IsTrue(candidate.eligible, candidate.reason);
            Assert.AreNotEqual(BusinessType.GeneralStore, candidate.businessType);
        }

        [Test]
        public void OwnedResidentialHoldingAllowsBusinessActivationWithFitWarnings()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryCreateOwnedResidentialHolding(out PlacedBuilding holding));

            Assert.IsTrue(context.TryFindActivationCandidateIndex(holding.id, BusinessType.Blacksmith, out int blacksmithOptionIndex));
            Assert.IsTrue(context.AcquisitionMarket.TryGetBusinessActivationCandidate(holding.id, blacksmithOptionIndex, out BusinessActivationCandidateState candidate));
            Assert.NotNull(candidate);
            Assert.AreEqual(BusinessType.Blacksmith, candidate.businessType);
            Assert.IsTrue(candidate.eligible, candidate.reason);
            Assert.Greater(candidate.costMultiplier, 1f);
            StringAssert.Contains("residential/non-workplace", candidate.fitWarningSummary);
            Assert.IsTrue(context.AcquisitionMarket.TryGetBusinessActivationQuote(holding.id, blacksmithOptionIndex, out ConstructionInputQuote quote, out _));
            Assert.NotNull(quote);
            Assert.IsTrue(quote.CanProceed, quote.BuildMissingSummary());

            bool started = context.AcquisitionMarket.TryStartBusinessFromOwnedShell(holding.id, blacksmithOptionIndex, out BusinessInstanceState business, out string message);

            Assert.IsTrue(started, message);
            Assert.NotNull(business);
            Assert.AreEqual(BusinessType.Blacksmith, business.BusinessType);
            StringAssert.Contains("Fit warnings", message);
        }

        [Test]
        public void OwnedLandListsOffZoneBuildOptionsAfterRecommendedOptions()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindOffRecommendationBuildOption(out int plotId, out int optionIndex, out PlayerDevelopmentFitResult fit));
            OwnedPlotBuildabilityState state = context.FindBuildabilityState(plotId);
            Assert.NotNull(state);

            bool sawOffRecommendation = false;
            for (int i = 0; i < state.buildOptions.Count; i++)
            {
                BuildingDefinition option = state.buildOptions[i];
                Assert.NotNull(option);
                bool recommended = option.CanUsePlot(state.plotZone);
                if (!recommended)
                {
                    sawOffRecommendation = true;
                    continue;
                }

                Assert.IsFalse(sawOffRecommendation, "Recommended shell options should appear before off-zone player-freedom options.");
            }

            Assert.GreaterOrEqual(optionIndex, 0);
            Assert.IsTrue(fit.HasWarnings);
            Assert.Greater(fit.costMultiplier, 1f);
        }

        [Test]
        public void OwnedLandCanConstructOffZoneShellThatPhysicallyFits()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindOffRecommendationBuildOption(out int plotId, out int optionIndex, out _));
            TownPlot plot = context.GetPlotById(plotId);
            Assert.NotNull(plot);

            bool built = context.AcquisitionMarket.TryConstructOnOwnedPlot(plotId, optionIndex, out PlacedBuilding shell, out string message);

            Assert.IsTrue(built, message);
            Assert.NotNull(shell);
            Assert.NotNull(shell.definition);
            Assert.IsTrue(shell.playerOwned);
            Assert.IsFalse(shell.definition.CanUsePlot(plot.zone), "Test should construct an option that old zoning rules would have blocked.");
        }

        [Test]
        public void OwnedLandHouseIntentListsHouseShellOptions()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out _, out _));

            IReadOnlyList<BuildingDefinition> options = context.AcquisitionMarket.GetHouseBuildOptionsForOwnedPlot(plotId);

            Assert.NotNull(options);
            Assert.Greater(options.Count, 0);
            Assert.NotNull(options[0]);
            Assert.IsTrue(options[0].CanHostHouseholds, "House intent should surface house-capable shells first.");
            Assert.IsTrue(context.AcquisitionMarket.TryGetHouseConstructionQuoteForOwnedPlotOption(plotId, 0, out ConstructionInputQuote quote, out string message), message);
            Assert.NotNull(quote);
            Assert.IsTrue(quote.CanProceed, quote.BuildMissingSummary());
        }

        [Test]
        public void OwnedLandHouseIntentConstructsResidentialShellNotBusinessFallback()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out _, out _));

            bool built = context.AcquisitionMarket.TryConstructHouseOnOwnedPlot(plotId, 0, out PlacedBuilding shell, out string message);

            Assert.IsTrue(built, message);
            Assert.NotNull(shell);
            Assert.NotNull(shell.definition);
            Assert.IsTrue(shell.definition.CanHostHouseholds);
            Assert.IsFalse(shell.definition.CanHostWorkplace, "The first house intent option should remain a house, not a business shell.");
        }

        [Test]
        public void OwnedLandBusinessIntentExposesSupportedBusinessTypesBeforeShellChoice()
        {
            using TestWorld context = TestWorld.Create();
            BusinessType[] expected =
            {
                BusinessType.Blacksmith,
                BusinessType.Butcher,
                BusinessType.Ranch,
                BusinessType.CropFarm,
                BusinessType.Sawmill,
                BusinessType.LumberYard,
                BusinessType.BoardingHouse,
                BusinessType.LiveryFreight,
                BusinessType.Builder,
                BusinessType.FuelDealer,
                BusinessType.GrainMill,
                BusinessType.Bakery,
                BusinessType.Tailor,
                BusinessType.Saloon,
                BusinessType.Barber,
                BusinessType.Wheelwright
            };
            List<BusinessType> actual = new();

            int count = context.AcquisitionMarket.GetBusinessDevelopmentCandidateCount();
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(context.AcquisitionMarket.TryGetBusinessDevelopmentCandidate(i, out BusinessActivationCandidateState candidate));
                Assert.NotNull(candidate);
                actual.Add(candidate.businessType);
            }

            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void FirstEligibleBusinessDevelopmentCandidateSkipsBlockedGeneralStore()
        {
            using TestWorld context = TestWorld.Create();

            int firstEligibleIndex = context.AcquisitionMarket.GetFirstEligibleBusinessDevelopmentCandidateIndex();

            Assert.GreaterOrEqual(firstEligibleIndex, 0);
            Assert.IsTrue(context.AcquisitionMarket.TryGetBusinessDevelopmentCandidate(firstEligibleIndex, out BusinessActivationCandidateState candidate));
            Assert.NotNull(candidate);
            Assert.IsTrue(candidate.eligible, candidate.reason);
            Assert.AreNotEqual(BusinessType.GeneralStore, candidate.businessType);
        }

        [Test]
        public void AdditionalGeneralStoreIsHiddenFromExpansionCandidateCycles()
        {
            using TestWorld context = TestWorld.Create();
            int developmentCount = context.AcquisitionMarket.GetBusinessDevelopmentCandidateCount();
            for (int i = 0; i < developmentCount; i++)
            {
                Assert.IsTrue(context.AcquisitionMarket.TryGetBusinessDevelopmentCandidate(i, out BusinessActivationCandidateState candidate));
                Assert.NotNull(candidate);
                Assert.AreNotEqual(BusinessType.GeneralStore, candidate.businessType);
            }

            Assert.IsTrue(context.TryBuildShell(out PlacedBuilding shell));
            int activationCount = context.AcquisitionMarket.GetBusinessActivationCandidateCount(shell.id);
            for (int i = 0; i < activationCount; i++)
            {
                Assert.IsTrue(context.AcquisitionMarket.TryGetBusinessActivationCandidate(shell.id, i, out BusinessActivationCandidateState candidate));
                Assert.NotNull(candidate);
                Assert.AreNotEqual(BusinessType.GeneralStore, candidate.businessType);
            }
        }

        [Test]
        public void FirstBusinessShellUsesPaidRegionalHardwareWhenBlacksmithHardwareIsMissing()
        {
            using TestWorld context = TestWorld.Create();
            context.SetBlacksmithHardwareStock(0);
            Assert.IsTrue(context.TryFindBusinessShellOption(BusinessType.Blacksmith, out int plotId, out int businessIndex, out int shellIndex, out _, out ConstructionInputQuote quote));
            ConstructionResourceQuoteLine hardware = quote.GetLine(ConstructionResourceKind.Nails);
            Assert.NotNull(hardware);
            StringAssert.Contains("Regional freight hardware", hardware.sourceLabel);
            StringAssert.Contains("higher landed cost", hardware.procurementNote);
            Assert.AreEqual(hardware.requiredUnits, hardware.availableUnits);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;
            int blacksmithCashBefore = context.GetBusiness(BusinessType.Blacksmith).RuntimeState.CurrentCashCents;

            bool built = context.AcquisitionMarket.TryConstructBusinessShellOnOwnedPlot(plotId, businessIndex, shellIndex, out PlacedBuilding shell, out string message);

            Assert.IsTrue(built, message);
            Assert.NotNull(shell);
            Assert.AreEqual(0, context.GetBlacksmithHardwareStock());
            Assert.AreEqual(ownerCashBefore - quote.CashCostCents, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(blacksmithCashBefore, context.GetBusiness(BusinessType.Blacksmith).RuntimeState.CurrentCashCents);
        }

        [Test]
        public void FirstBusinessFitOutUsesPaidRegionalHardwareWhenBlacksmithHardwareIsMissing()
        {
            using TestWorld context = TestWorld.Create();
            context.SetBlacksmithHardwareStock(0);
            Assert.IsTrue(context.TryFindBusinessShellOption(BusinessType.Blacksmith, out int plotId, out int businessIndex, out int shellIndex, out _, out _));
            Assert.IsTrue(context.AcquisitionMarket.TryConstructBusinessShellOnOwnedPlot(plotId, businessIndex, shellIndex, out PlacedBuilding shell, out string buildMessage), buildMessage);
            Assert.IsTrue(context.TryFindActivationOption(shell.id, out int activationIndex, out BusinessActivationCandidateState candidate, out ConstructionInputQuote quote));
            Assert.AreNotEqual(BusinessType.GeneralStore, candidate.businessType);
            ConstructionResourceQuoteLine hardware = quote.GetLine(ConstructionResourceKind.Nails);
            Assert.NotNull(hardware);
            StringAssert.Contains("Regional freight hardware", hardware.sourceLabel);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;

            bool started = context.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, activationIndex, out BusinessInstanceState business, out string startMessage);

            Assert.IsTrue(started, startMessage);
            Assert.NotNull(business);
            Assert.AreEqual(0, context.GetBlacksmithHardwareStock());
            Assert.AreEqual(ownerCashBefore - quote.CashCostCents, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(1, context.AcquisitionMarket.OwnedBusinessCount);
        }

        [Test]
        public void FirstBusinessShellPrefersAndConsumesActiveBlacksmithStock()
        {
            using TestWorld context = TestWorld.Create();
            context.SetBlacksmithHardwareStock(1000);
            Assert.IsTrue(context.TryFindBusinessShellOption(BusinessType.Blacksmith, out int plotId, out int businessIndex, out int shellIndex, out _, out ConstructionInputQuote quote));
            ConstructionResourceQuoteLine hardware = quote.GetLine(ConstructionResourceKind.Nails);
            Assert.NotNull(hardware);
            Assert.IsFalse(hardware.sourceLabel.Contains("Regional freight hardware"), hardware.sourceLabel);
            int hardwareBefore = context.GetBlacksmithHardwareStock();

            bool built = context.AcquisitionMarket.TryConstructBusinessShellOnOwnedPlot(plotId, businessIndex, shellIndex, out _, out string message);

            Assert.IsTrue(built, message);
            Assert.AreEqual(hardwareBefore - hardware.requiredUnits, context.GetBlacksmithHardwareStock());
        }

        [Test]
        public void LaterBlacksmithRecoveryShellUsesRegionalHardwareAfterFirstExpansion()
        {
            using TestWorld context = TestWorld.Create();
            context.SetBlacksmithHardwareStock(0);
            Assert.IsTrue(context.TryFindBusinessShellOption(BusinessType.Blacksmith, out int plotId, out int businessIndex, out int shellIndex, out _, out _));
            Assert.IsTrue(context.AcquisitionMarket.TryConstructBusinessShellOnOwnedPlot(plotId, businessIndex, shellIndex, out PlacedBuilding shell, out string buildMessage), buildMessage);
            Assert.IsTrue(context.TryFindActivationOption(shell.id, out int activationIndex, out _, out _));
            Assert.IsTrue(context.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, activationIndex, out _, out string startMessage), startMessage);
            Assert.AreEqual(1, context.AcquisitionMarket.OwnedBusinessCount);
            context.SetBlacksmithHardwareStock(0);
            Assert.IsTrue(context.TryMarkNextBuildableEmptyPlotOwned(out _));

            Assert.IsTrue(context.TryFindBusinessShellQuote(
                BusinessType.Blacksmith,
                false,
                out int recoveryPlotId,
                out int recoveryBusinessIndex,
                out int recoveryShellIndex,
                out _,
                out ConstructionInputQuote quote,
                out string quoteMessage), quoteMessage);

            Assert.NotNull(quote);
            ConstructionResourceQuoteLine hardware = quote.GetLine(ConstructionResourceKind.Nails);
            Assert.NotNull(hardware);
            Assert.IsTrue(quote.CanProceed, quote.BuildMissingSummary());
            StringAssert.Contains("Regional freight hardware", hardware.sourceLabel);
            Assert.AreEqual(hardware.requiredUnits, hardware.availableUnits);

            bool built = context.AcquisitionMarket.TryConstructBusinessShellOnOwnedPlot(
                recoveryPlotId,
                recoveryBusinessIndex,
                recoveryShellIndex,
                out PlacedBuilding secondShell,
                out string secondBuildMessage);

            Assert.IsTrue(built, secondBuildMessage);
            Assert.NotNull(secondShell);
            Assert.AreEqual(0, context.GetBlacksmithHardwareStock());
        }

        [Test]
        public void OwnedLandBusinessShellOptionsOnlyExposeSuitableWorkplaceShells()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBusinessDevelopmentCandidateIndex(BusinessType.Blacksmith, out int businessIndex));
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out _, out _));

            IReadOnlyList<BuildingDefinition> options = context.AcquisitionMarket.GetBusinessBuildOptionsForOwnedPlot(plotId, businessIndex);

            Assert.NotNull(options);
            Assert.Greater(options.Count, 0);
            for (int i = 0; i < options.Count; i++)
            {
                BuildingDefinition option = options[i];
                Assert.NotNull(option);
                Assert.IsTrue(option.CanHostWorkplace, "Business shell options should not include residential fallback shells.");
                Assert.IsTrue(option.IsSuitableForBusiness(BusinessType.Blacksmith), "Business shell options should be suitable for the selected business type.");
            }
        }

        [Test]
        public void OwnedLandBusinessIntentCanConstructSelectedBusinessShell()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBusinessShellOption(BusinessType.Blacksmith, out int plotId, out int businessIndex, out int shellIndex, out BusinessActivationCandidateState candidate, out ConstructionInputQuote quote));
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;
            int storeCashBefore = context.StoreRuntime.CurrentCashCents;

            bool built = context.AcquisitionMarket.TryConstructBusinessShellOnOwnedPlot(plotId, businessIndex, shellIndex, out PlacedBuilding shell, out string message);

            Assert.IsTrue(built, message);
            Assert.NotNull(shell);
            Assert.NotNull(shell.definition);
            Assert.IsTrue(shell.playerOwned);
            Assert.AreEqual(BusinessType.Blacksmith, candidate.businessType);
            Assert.IsTrue(shell.definition.IsSuitableForBusiness(candidate.businessType));
            Assert.AreEqual(ownerCashBefore - quote.CashCostCents, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(storeCashBefore, context.StoreRuntime.CurrentCashCents);
            StringAssert.Contains($"Business intent: {candidate.displayName}", message);
        }

        [Test]
        public void LumberYardEarnsWeeklyRevenueFromLocalBuilderDemand()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState yard = context.GetBusiness(BusinessType.LumberYard);
            CategoryStockState lumber = yard.RuntimeState.GetCategoryStock("lumber");
            Assert.NotNull(lumber);
            int stockBefore = Mathf.Max(80, lumber.TargetStockUnits);
            context.SetLumberYardStock(stockBefore);
            context.SetBusinessCash(yard, 50000);

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.Greater(yard.RuntimeState.LastWeeklyLocalTransferRevenueCents, 0);
            Assert.Less(context.GetLumberYardStock(), stockBefore);
            StringAssert.Contains("local builder demand", yard.RuntimeState.LastWeeklyOperationSummary);
        }

        [Test]
        public void OwnedLandReadinessShowsShellStartupAndInputs()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBusinessShellOption(BusinessType.Blacksmith, out int plotId, out int businessIndex, out int shellIndex, out BusinessActivationCandidateState candidate, out ConstructionInputQuote quote));
            ExpansionReadinessService readinessService = new(context.AcquisitionMarket, context.PlayerPortfolio, null, context.StoreRuntime, context.SharedBusinessRuntime);

            ExpansionReadinessResult readiness = readinessService.BuildForOwnedLand(plotId, businessIndex, shellIndex);

            Assert.AreEqual(ExpansionReadinessKind.OwnedLand, readiness.Kind);
            Assert.AreEqual(BusinessType.Blacksmith, readiness.SelectedBusinessType);
            Assert.GreaterOrEqual(readiness.EstimatedTotalPathCostCents, quote.CashCostCents + candidate.startupCostCents);
            Assert.Greater(readiness.RequiredInputs.Count, 0);
            StringAssert.Contains("build shell", readiness.RecommendedAction);
        }

        [Test]
        public void OwnedLandReadinessReportsMissingConstructionInputs()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBusinessShellOption(BusinessType.Blacksmith, out int plotId, out int businessIndex, out int shellIndex, out _, out _));
            context.SetLumberYardStock(0);
            context.SetSawmillStock(0);
            ExpansionReadinessService readinessService = new(context.AcquisitionMarket, context.PlayerPortfolio, null, context.StoreRuntime, context.SharedBusinessRuntime);

            ExpansionReadinessResult readiness = readinessService.BuildForOwnedLand(plotId, businessIndex, shellIndex);

            Assert.IsTrue(readiness.HasHardBlockers);
            StringAssert.Contains("Lumber", string.Join("; ", readiness.HardBlockers));
        }

        [Test]
        public void VacantShellReadinessShowsFitOutCostAndBusinessType()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryBuildShell(out PlacedBuilding shell));
            Assert.IsTrue(context.TryFindActivationOption(shell.id, out int optionIndex, out BusinessActivationCandidateState candidate, out ConstructionInputQuote quote));
            ExpansionReadinessService readinessService = new(context.AcquisitionMarket, context.PlayerPortfolio, null, context.StoreRuntime, context.SharedBusinessRuntime);

            ExpansionReadinessResult readiness = readinessService.BuildForVacantShell(shell.id, optionIndex);

            Assert.AreEqual(ExpansionReadinessKind.VacantShell, readiness.Kind);
            Assert.AreEqual(candidate.businessType, readiness.SelectedBusinessType);
            Assert.AreEqual(quote.CashCostCents, readiness.CashRequiredNowCents);
            Assert.Greater(readiness.RequiredInputs.Count, 0);
            StringAssert.Contains("start business", readiness.RecommendedAction);
        }

        [Test]
        public void ConstructionInputAvailabilitySummaryListsCurrentSources()
        {
            using TestWorld context = TestWorld.Create();

            string summary = context.AcquisitionMarket.BuildConstructionInputAvailabilityText();

            StringAssert.Contains("Construction Inputs", summary);
            StringAssert.Contains("Lumber", summary);
            StringAssert.Contains("Nails / simple hardware", summary);
            StringAssert.Contains("Labor", summary);
            StringAssert.Contains("Sawmill property", summary);
            StringAssert.Contains("standing timber", summary);
            StringAssert.Contains("staged logs", summary);
        }

        [Test]
        public void ConstructionInputAvailabilitySummaryDoesNotRewriteSawmillStockFromLumberYard()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(5);
            context.SetLumberYardStock(90);
            int sawmillBefore = context.GetSawmillStock();

            string summary = context.AcquisitionMarket.BuildConstructionInputAvailabilityText();

            StringAssert.Contains("Construction Inputs", summary);
            StringAssert.Contains("Lumber", summary);
            Assert.AreEqual(sawmillBefore, context.GetSawmillStock());
            Assert.AreEqual(90, context.GetLumberYardStock());
        }

        [Test]
        public void ConstructionCanConsumeLumberYardStockWithoutChangingSawmillStock()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(0);
            context.SetLumberYardStock(220);
            context.SetBlacksmithHardwareStock(100);
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out ConstructionInputQuote quote));
            ConstructionResourceQuoteLine lumber = quote.GetLine(ConstructionResourceKind.Lumber);
            Assert.NotNull(lumber);
            Assert.IsTrue(lumber.HasSourceAllocations);
            StringAssert.Contains("Lumber Yard", lumber.sourceLabel);
            int sawmillBefore = context.GetSawmillStock();
            int yardBefore = context.GetLumberYardStock();

            bool built = context.AcquisitionMarket.TryConstructOnOwnedPlot(plotId, optionIndex, out PlacedBuilding building, out string message);

            Assert.IsTrue(built, message);
            Assert.NotNull(building);
            Assert.AreEqual(sawmillBefore, context.GetSawmillStock());
            Assert.AreEqual(yardBefore - lumber.requiredUnits, context.GetLumberYardStock());
        }

        [Test]
        public void ConstructionQuotePrefersTownFacingLumberYardWhenBothLumberSellersAreStocked()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(220);
            context.SetLumberYardStock(220);
            context.SetBlacksmithHardwareStock(100);

            Assert.IsTrue(context.TryFindBuildableOption(out _, out _, out ConstructionInputQuote quote));
            ConstructionResourceQuoteLine lumber = quote.GetLine(ConstructionResourceKind.Lumber);

            Assert.NotNull(lumber);
            Assert.IsTrue(lumber.HasSourceAllocations);
            Assert.AreEqual(BusinessType.LumberYard, lumber.sourceAllocations[0].businessType);
            StringAssert.Contains("Lumber Yard", lumber.sourceLabel);
        }

        [Test]
        public void WeeklyOperationsTransferSawmillLumberToLumberYard()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(200);
            context.SetLumberYardStock(0);
            context.SetBlacksmithHardwareStock(20);
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            BusinessInstanceState yard = context.GetBusiness(BusinessType.LumberYard);

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.Greater(context.GetLumberYardStock(), 0);
            Assert.Greater(sawmill.RuntimeState.LastWeeklyLocalTransferRevenueCents, 0);
            Assert.Greater(yard.RuntimeState.LastWeeklyLocalTransferCostCents, 0);
            StringAssert.Contains("lumber", yard.RuntimeState.LastWeeklyTransferSummary);
        }

        [Test]
        public void LumberYardBuysOffMapLumberWhenLocalSawmillCannotSupply()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(0);
            context.SetSawmillProductionState(0, 0, 0, 0, 4, 360, 1, true);
            context.SetLumberYardStock(0);
            BusinessInstanceState yard = context.GetBusiness(BusinessType.LumberYard);
            context.SetBusinessCash(yard, 50000);

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.Greater(context.GetLumberYardStock(), 0);
            Assert.Greater(yard.RuntimeState.LastWeeklyLocalTransferCostCents, 0);
            StringAssert.Contains("off-map lumber", yard.RuntimeState.LastWeeklyTransferSummary);
        }

        [Test]
        public void LumberYardCashReserveLimitsLocalAndOffMapBuying()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(200);
            context.SetLumberYardStock(0);
            BusinessInstanceState yard = context.GetBusiness(BusinessType.LumberYard);
            context.SetBusinessCash(yard, context.GetFilledWeeklyPayrollCents(yard));

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.AreEqual(0, context.GetLumberYardStock());
            StringAssert.Contains("cash-limited", yard.RuntimeState.LastWeeklyBlockedReason);
        }

        [Test]
        public void WeeklySawmillProductionAddsLumberConsumesHardwareAndRecordsStatus()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(0);
            context.SetSawmillProductionState(80, 8, 10, 10, 4, 360, 1, false);
            context.SetBlacksmithHardwareStock(10);
            int hardwareBefore = context.GetBlacksmithHardwareStock();

            bool produced = context.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction();

            Assert.IsTrue(produced);
            Assert.AreEqual(28, context.GetSawmillStock());
            Assert.AreEqual(75, context.GetSawmillStandingTimber());
            Assert.AreEqual(6, context.GetSawmillLogs());
            Assert.AreEqual(hardwareBefore - 1, context.GetBlacksmithHardwareStock());
            Assert.AreEqual(7, context.GetSawmillNode().LastWeeklySlabsOffcutsProducedUnits);
            Assert.AreEqual(7, context.GetSawmillNode().LastWeeklySlabsOffcutsSoldUnits);
            StringAssert.Contains("produced 28 lumber", context.GetSawmillNode().LastWeeklyProductionSummary);
            StringAssert.Contains("owned hauling", context.GetSawmillNode().LastWeeklyHaulingMode);
            StringAssert.Contains("commuting remote crew", context.AcquisitionMarket.BuildConstructionInputAvailabilityText());
        }

        [Test]
        public void WeeklySawmillUsesPaidRegionalHardwareWhenBlacksmithHardwareIsMissing()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(0);
            context.SetSawmillProductionState(80, 8, 10, 10, 4, 360, 1, false);
            context.SetBlacksmithHardwareStock(0);
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            context.SetBusinessCash(sawmill, 50000);
            int cashBefore = sawmill.RuntimeState.CurrentCashCents;

            bool produced = context.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction();

            Assert.IsTrue(produced);
            Assert.Greater(context.GetSawmillStock(), 0);
            Assert.AreEqual(75, context.GetSawmillStandingTimber());
            Assert.Greater(context.GetSawmillLogs(), 0);
            Assert.AreEqual(0, context.GetBlacksmithHardwareStock());
            Assert.Less(sawmill.RuntimeState.CurrentCashCents, cashBefore);
            StringAssert.Contains("Regional freight hardware", context.GetSawmillNode().LastWeeklyProductionSummary);
            Assert.IsFalse(context.GetSawmillNode().LastWeeklyBlockedReason.Contains("hardware"));
        }

        [Test]
        public void WeeklySawmillMaintenanceWeakensWhenRegionalHardwareIsUnaffordable()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(0);
            context.SetSawmillProductionState(80, 8, 10, 10, 4, 360, 1, false);
            context.SetBlacksmithHardwareStock(0);
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            context.SetBusinessCash(sawmill, 0);
            float conditionBefore = sawmill.RuntimeState.Reliability01;

            bool produced = context.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction();

            Assert.IsTrue(produced);
            Assert.Less(sawmill.RuntimeState.Reliability01, conditionBefore);
            Assert.AreEqual(0, context.GetBlacksmithHardwareStock());
            Assert.AreEqual(0, sawmill.RuntimeState.CurrentCashCents);
            StringAssert.Contains("hardware", context.GetSawmillNode().LastWeeklyBlockedReason);
            StringAssert.Contains("Regional freight hardware", context.GetSawmillNode().LastWeeklyBlockedReason);
        }

        [Test]
        public void WeeklySawmillUsesHiredHaulingFallbackWhenInternalHaulingIsMissing()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(0);
            context.SetSawmillProductionState(80, 20, 0, 20, 4, 360, 1, false);
            context.SetSawmillHauling(false, true, 35);
            context.SetBlacksmithHardwareStock(10);
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            int cashBefore = sawmill.RuntimeState.CurrentCashCents;

            bool produced = context.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction();

            Assert.IsTrue(produced);
            StringAssert.Contains("hired hauling", context.GetSawmillNode().LastWeeklyHaulingMode);
            Assert.Greater(context.GetSawmillNode().LastWeeklyHaulingCostCents, 0);
            Assert.Less(sawmill.RuntimeState.CurrentCashCents, cashBefore + sawmill.RuntimeState.LastWeeklyLocalTransferRevenueCents);
        }

        [Test]
        public void WeeklySawmillBlocksMillingWhenMaintenanceConditionIsTooLow()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(0);
            context.SetSawmillProductionState(80, 8, 10, 10, 4, 360, 1, false);
            context.SetBlacksmithHardwareStock(0);
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            sawmill.RuntimeState.AdjustReliability01(-0.80f);

            bool produced = context.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction();

            Assert.IsFalse(produced);
            Assert.AreEqual(0, context.GetSawmillStock());
            StringAssert.Contains("condition", context.GetSawmillNode().LastWeeklyBlockedReason);
        }

        [Test]
        public void WeeklySawmillProductionUsesBusinessStockAuthorityAndMirrorsSupportNode()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            context.SetSawmillStock(0);
            context.SetSawmillProductionState(0, 0, 10, 10, 4, 360, 1, false);
            context.SetBusinessStock(sawmill, "standing_timber", 80);
            context.SetBusinessStock(sawmill, "sawmill_logs", 8);
            context.SetBlacksmithHardwareStock(10);

            bool produced = context.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction();

            Assert.IsTrue(produced);
            Assert.AreEqual(context.GetBusinessStock(sawmill, "standing_timber"), context.GetSawmillStandingTimber());
            Assert.AreEqual(context.GetBusinessStock(sawmill, "sawmill_logs"), context.GetSawmillLogs());
            Assert.AreEqual(context.GetBusinessStock(sawmill, "lumber"), context.GetSawmillStock());
            Assert.Greater(context.GetSawmillStock(), 0);
        }

        [Test]
        public void WeeklySawmillProductionFallsWithThinLoggingCrew()
        {
            using TestWorld staffed = TestWorld.Create();
            staffed.SetSawmillStock(0);
            staffed.SetSawmillProductionState(80, 0, 20, 20, 4, 360, 1, false);
            staffed.SetBlacksmithHardwareStock(10);
            Assert.IsTrue(staffed.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction());
            int staffedOutput = staffed.GetSawmillStock();

            using TestWorld thinCrew = TestWorld.Create();
            thinCrew.SetSawmillStock(0);
            thinCrew.SetSawmillProductionState(80, 0, 20, 20, 4, 360, 1, false);
            thinCrew.SetBlacksmithHardwareStock(10);
            BusinessInstanceState thinCrewSawmill = thinCrew.GetBusiness(BusinessType.Sawmill);
            thinCrew.SetWorkerSlotFilled(thinCrewSawmill, "logger", false);

            bool produced = thinCrew.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction();

            Assert.IsTrue(produced);
            Assert.Greater(staffedOutput, thinCrew.GetSawmillStock());
            Assert.Greater(thinCrew.GetSawmillStock(), 0);
        }

        [Test]
        public void WeeklySawmillDoesNotProduceImpossibleOutputWithoutTimberOrLogs()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            context.SetSawmillStock(0);
            context.SetSawmillProductionState(0, 0, 10, 10, 4, 360, 1, false);
            context.SetBlacksmithHardwareStock(10);

            bool produced = context.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction();

            Assert.IsFalse(produced);
            Assert.AreEqual(0, context.GetSawmillStock());
            Assert.AreEqual(0, context.GetBusinessStock(sawmill, "lumber"));
            Assert.AreEqual(0, context.GetBusinessStock(sawmill, "sawmill_logs"));
            StringAssert.Contains("standing timber depleted", context.GetSawmillNode().LastWeeklyBlockedReason);
            StringAssert.Contains("no staged logs", context.GetSawmillNode().LastWeeklyBlockedReason);
        }

        [Test]
        public void SawmillInventoryTextShowsRunContextAndAuthoritativeStock()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(0);
            context.SetSawmillProductionState(80, 8, 10, 10, 4, 360, 1, false);
            context.SetBlacksmithHardwareStock(10);
            Assert.IsTrue(context.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction());
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);

            string text = context.SharedBusinessRuntime.BuildBusinessInventoryText(sawmill);

            StringAssert.Contains("Yard context:", text);
            StringAssert.Contains("Last saw run:", text);
            StringAssert.Contains("Standing Timber", text);
            StringAssert.Contains("Slabs / Offcuts", text);
        }

        [Test]
        public void ConstructionCanConsumeLumberProducedByWeeklySawmill()
        {
            using TestWorld context = TestWorld.Create();
            context.SetLumberYardStock(0);
            context.SetSawmillStock(0);
            context.SetSawmillProductionState(80, 40, 0, 40, 4, 360, 1, false);
            context.SetBlacksmithHardwareStock(40);
            Assert.IsTrue(context.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction());
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out ConstructionInputQuote quote));
            int lumberBefore = context.GetSawmillStock();

            bool built = context.AcquisitionMarket.TryConstructOnOwnedPlot(plotId, optionIndex, out PlacedBuilding building, out string message);

            Assert.IsTrue(built, message);
            Assert.NotNull(building);
            Assert.AreEqual(lumberBefore - quote.GetLine(ConstructionResourceKind.Lumber).requiredUnits, context.GetSawmillStock());
        }

        [Test]
        public void GeneratedTownCreatesRemoteSawmillYardWithLongSpurAndDressing()
        {
            using TestWorld context = TestWorld.Create();

            TownPlot sawmill = context.FindSawmillPlot();

            Assert.NotNull(sawmill);
            Assert.AreEqual(PlotZone.Agricultural, sawmill.zone);
            Assert.AreEqual(AgriculturalSiteRole.SawmillYard, sawmill.agriculturalSiteRole);
            Assert.GreaterOrEqual(sawmill.siteSizeCells.x, 40);
            Assert.GreaterOrEqual(sawmill.siteSizeCells.y, 34);
            Assert.GreaterOrEqual(Mathf.Abs(sawmill.bounds.Center.z - context.TownWorld.Grid.Depth / 2), 50);
            Assert.AreEqual(RoadType.Spur, context.TownWorld.Grid.GetCell(sawmill.roadAccessCell).roadType);
            Assert.GreaterOrEqual(sawmill.buildingId, 0);
            Assert.IsTrue(context.VisualNameContains("Remote Sawmill Log Deck"));
            Assert.IsTrue(context.VisualNameContains("Remote Sawmill Small Sawmill Main Mill"));
            Assert.IsTrue(context.VisualNameContains("Remote Sawmill Worker House 1"));
            Assert.IsTrue(context.VisualNameContains("Remote Sawmill Worker House 2"));
            Assert.IsTrue(context.VisualNameContains("Remote Sawmill Worker House 3"));
            Assert.IsTrue(context.VisualNameContains("Remote Sawmill Placeholder Tree Block"));
            Assert.AreEqual(sawmill.id, context.GetSawmillNode().RemotePropertyPlotId);
        }

        [Test]
        public void SharedWeeklyOperationsProduceAndTransferCoreSupplies()
        {
            using TestWorld context = TestWorld.Create();
            context.SetBlacksmithHardwareStock(20);
            context.SetStoreStockToTarget("tools_hardware");
            context.SetStoreStock("meat", 0);
            int hardwareBefore = context.GetBlacksmithHardwareStock();
            int storeMeatBefore = context.GetStoreStock("meat");

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.Greater(context.GetBlacksmithHardwareStock(), hardwareBefore);
            Assert.Greater(context.GetStoreStock("meat"), storeMeatBefore);
            BusinessInstanceState blacksmith = context.SharedBusinessRuntime.FindByType(BusinessType.Blacksmith);
            StringAssert.Contains("tools_hardware", context.SharedBusinessRuntime.GetLastOperationSummary(blacksmith));
            BusinessInstanceState sawmill = context.SharedBusinessRuntime.FindByType(BusinessType.Sawmill);
            Assert.NotNull(sawmill);
            StringAssert.Contains("lumber", context.SharedBusinessRuntime.GetLastOperationSummary(sawmill));
        }

        [Test]
        public void SharedWeeklySettlementPaysPayrollConsumesInputsProducesOutputsAndRecordsCash()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState blacksmith = context.GetBusiness(BusinessType.Blacksmith);
            context.SetBusinessStock(blacksmith, "metal_inputs", 20);
            context.SetBusinessStock(blacksmith, "tools_hardware", 0);
            int cashBefore = blacksmith.RuntimeState.CurrentCashCents;

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.AreEqual(cashBefore, blacksmith.RuntimeState.LastWeeklyCashBeforeCents);
            Assert.Greater(blacksmith.RuntimeState.LastWeeklyPayrollCents, 0);
            Assert.Greater(blacksmith.RuntimeState.LastWeeklyInputUnitsConsumed, 0);
            Assert.Greater(blacksmith.RuntimeState.LastWeeklyOutputUnitsProduced, 0);
            Assert.AreEqual(blacksmith.RuntimeState.CurrentCashCents, blacksmith.RuntimeState.LastWeeklyCashAfterCents);
            StringAssert.Contains("tools_hardware", blacksmith.RuntimeState.LastWeeklyOperationSummary);
        }

        [Test]
        public void PlayerOwnedBusinessDistributionPaysOwnerCashAfterCheckpointedGrowth()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState blacksmith = context.GetBusiness(BusinessType.Blacksmith);
            Assert.IsTrue(context.SharedBusinessRuntime.TryTransferBusinessToPlayer(blacksmith.AssignedBuildingId, out _, out string transferMessage), transferMessage);
            context.SetBusinessCash(blacksmith, 50000);
            context.PlayerPortfolio.RegisterCurrentBusinessCashCheckpoint(blacksmith, 1);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;

            blacksmith.RuntimeState.AddCashCents(20000);

            int distributed = context.PlayerPortfolio.ResolveOwnerDistribution(blacksmith, 10000, 5, "Blacksmith");

            Assert.AreEqual(5000, distributed);
            Assert.AreEqual(ownerCashBefore + 5000, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(65000, blacksmith.RuntimeState.CurrentCashCents);
        }

        [Test]
        public void PlayerOwnedSawmillDistributionPaysOwnerCashAfterCheckpointedGrowth()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);
            Assert.IsTrue(context.SharedBusinessRuntime.TryTransferBusinessToPlayer(sawmill.AssignedBuildingId, out _, out string transferMessage), transferMessage);
            context.SetBusinessCash(sawmill, 50000);
            context.PlayerPortfolio.RegisterCurrentBusinessCashCheckpoint(sawmill, 1);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;

            sawmill.RuntimeState.AddCashCents(20000);

            int distributed = context.PlayerPortfolio.ResolveOwnerDistribution(sawmill, 10000, 5, "Small Sawmill");

            Assert.AreEqual(5000, distributed);
            Assert.AreEqual(ownerCashBefore + 5000, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(65000, sawmill.RuntimeState.CurrentCashCents);
        }

        [Test]
        public void TownOwnedBusinessDoesNotDistributeToOwnerCash()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState butcher = context.GetBusiness(BusinessType.Butcher);
            context.SetBusinessCash(butcher, 50000);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;

            butcher.RuntimeState.AddCashCents(20000);

            int distributed = context.PlayerPortfolio.ResolveOwnerDistribution(butcher, 0, 2, "Butcher");

            Assert.AreEqual(0, distributed);
            Assert.AreEqual(ownerCashBefore, context.PlayerPortfolio.OwnerCashCents);
        }

        [Test]
        public void LegacyPortfolioMigrationMovesOnlyGeneralStoreSurplusAboveReserve()
        {
            using TestWorld context = TestWorld.Create();
            int reserve = context.StoreRuntime.ProtectedBusinessCashReserveCents;
            context.SetStoreCash(reserve + 5000);
            GameObject portfolioObject = new("Legacy Portfolio Migration Test");
            try
            {
                PlayerPortfolioManager legacyPortfolio = portfolioObject.AddComponent<PlayerPortfolioManager>();
                legacyPortfolio.Configure(null, null);

                legacyPortfolio.InitializeFromLegacyBusinessCash(context.StoreRuntime, context.SharedBusinessRuntime);

                Assert.AreEqual(5000, legacyPortfolio.OwnerCashCents);
                Assert.AreEqual(reserve, context.StoreRuntime.CurrentCashCents);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(portfolioObject);
            }
        }

        [Test]
        public void PortfolioSaveDtoRestoresOwnerCashAndDistributionCheckpoints()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState blacksmith = context.GetBusiness(BusinessType.Blacksmith);
            Assert.IsTrue(context.SharedBusinessRuntime.TryTransferBusinessToPlayer(blacksmith.AssignedBuildingId, out _, out string transferMessage), transferMessage);
            context.PlayerPortfolio.RegisterCurrentBusinessCashCheckpoint(blacksmith, 3);
            PlayerPortfolioSaveDto dto = context.PlayerPortfolio.CaptureSaveDto();
            GameObject portfolioObject = new("Portfolio Save Restore Test");
            try
            {
                PlayerPortfolioManager restored = portfolioObject.AddComponent<PlayerPortfolioManager>();

                restored.LoadFromSaveDto(dto);

                Assert.AreEqual(context.PlayerPortfolio.OwnerCashCents, restored.OwnerCashCents);
                Assert.GreaterOrEqual(restored.DistributionCheckpoints.Count, 1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(portfolioObject);
            }
        }

        [Test]
        public void SharedRuntimeDoesNotDistributeDeepGeneralStoreDuplicate()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState storeBusiness = context.StoreRuntime.CurrentBusiness;
            Assert.NotNull(storeBusiness);
            context.SetStoreCash(50000);
            context.PlayerPortfolio.RegisterCurrentBusinessCashCheckpoint(storeBusiness, 1);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;

            storeBusiness.RuntimeState.AddCashCents(20000);
            int distributed = context.PlayerPortfolio.ResolveOwnerDistribution(storeBusiness, 0, 5, "General Store");
            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.Greater(distributed, 0);
            Assert.AreEqual(ownerCashBefore + distributed, context.PlayerPortfolio.OwnerCashCents);
        }

        [Test]
        public void PaidLocalTransfersIncreaseSourceCashAndDecreaseDestinationCash()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState ranch = context.GetBusiness(BusinessType.Ranch);
            BusinessInstanceState butcher = context.GetBusiness(BusinessType.Butcher);
            context.SetBusinessStock(ranch, "livestock_inputs", 20);
            context.SetBusinessStock(butcher, "livestock_inputs", 0);
            int butcherCashBefore = butcher.RuntimeState.CurrentCashCents;

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.Greater(ranch.RuntimeState.LastWeeklyLocalTransferRevenueCents, 0);
            Assert.Greater(butcher.RuntimeState.LastWeeklyLocalTransferCostCents, 0);
            Assert.Less(butcher.RuntimeState.CurrentCashCents, butcherCashBefore);
            StringAssert.Contains("sold", ranch.RuntimeState.LastWeeklyTransferSummary);
            StringAssert.Contains("bought", butcher.RuntimeState.LastWeeklyTransferSummary);
        }

        [Test]
        public void WeeklySharedOperationsCreatesRecurringLocalOrderRelationships()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState ranch = context.GetBusiness(BusinessType.Ranch);
            BusinessInstanceState butcher = context.GetBusiness(BusinessType.Butcher);
            context.SetBusinessStock(ranch, "livestock_inputs", 20);
            context.SetBusinessStock(butcher, "livestock_inputs", 0);
            context.SetBusinessCash(butcher, 50000);

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            LocalRecurringOrderRelationshipState relationship = null;
            IReadOnlyList<LocalRecurringOrderRelationshipState> relationships = context.SharedBusinessRuntime.LocalOrderRelationships;
            for (int i = 0; i < relationships.Count; i++)
            {
                if (relationships[i] != null
                    && relationships[i].OrderId == "ranch_livestock_to_butcher"
                    && relationships[i].SellerInstanceId == ranch.InstanceId
                    && relationships[i].BuyerInstanceId == butcher.InstanceId)
                {
                    relationship = relationships[i];
                    break;
                }
            }

            Assert.NotNull(relationship);
            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Clean, relationship.LastStatus);
            Assert.Greater(relationship.LastFulfilledUnits, 0);
            Assert.AreEqual(1, relationship.CleanFulfillmentCount);
            StringAssert.Contains("Recurring local orders", context.SharedBusinessRuntime.LastWeeklyRecurringLocalOrderSummary);
            StringAssert.Contains("ranch_livestock_to_butcher", relationship.RelationshipId);
        }

        [Test]
        public void InputShortageBlocksWeeklyOutputWithReadableReason()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState ranch = context.GetBusiness(BusinessType.Ranch);
            BusinessInstanceState butcher = context.GetBusiness(BusinessType.Butcher);
            context.SetBusinessCash(ranch, 0);
            context.SetBusinessStock(ranch, "feed_inputs", 0);
            context.SetBusinessStock(ranch, "livestock_inputs", 0);
            context.SetBusinessStock(butcher, "livestock_inputs", 0);
            context.SetBusinessStock(butcher, "meat", 0);
            context.SetBusinessCash(butcher, context.GetFilledWeeklyPayrollCents(butcher));

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.AreEqual(0, butcher.RuntimeState.LastWeeklyOutputUnitsProduced);
            StringAssert.Contains("input", butcher.RuntimeState.LastWeeklyBlockedReason);
            StringAssert.Contains("livestock_inputs", butcher.RuntimeState.LastWeeklyBlockedReason);
        }

        [Test]
        public void GeneralStorePaidLocalSupplyIntakeSpendsCashAndRecordsSummary()
        {
            using TestWorld context = TestWorld.Create();
            context.SetStoreStock("meat", 0);
            context.SetStoreCash(10000);
            int cashBefore = context.StoreRuntime.CurrentCashCents;

            int accepted = context.StoreRuntime.ReceiveLocalSupply("meat", 4, 175, "Test Butcher");

            Assert.AreEqual(4, accepted);
            Assert.AreEqual(4, context.GetStoreStock("meat"));
            Assert.AreEqual(cashBefore - 700, context.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(700, context.StoreRuntime.LastWeeklyLocalSupplySpendCents);
            Assert.AreEqual(4, context.StoreRuntime.LastWeeklyLocalSupplyUnitsReceived);
            StringAssert.Contains("Test Butcher", context.StoreRuntime.LastWeeklyLocalSupplySummary);
            Assert.AreEqual(700, context.StoreRuntime.RuntimeState.LastWeeklyLocalTransferCostCents);
        }

        [Test]
        public void SharedBusinessesRemainSolventAfterRepeatedWeeklySettlements()
        {
            using TestWorld context = TestWorld.Create();
            BusinessType[] stableTypes =
            {
                BusinessType.Blacksmith,
                BusinessType.Butcher,
                BusinessType.CropFarm,
                BusinessType.Ranch
            };

            for (int week = 0; week < 12; week++)
            {
                context.SharedBusinessRuntime.ResolveWeeklySharedOperations();
            }

            for (int i = 0; i < stableTypes.Length; i++)
            {
                BusinessInstanceState business = context.GetBusiness(stableTypes[i]);
                Assert.GreaterOrEqual(
                    business.RuntimeState.CurrentCashCents,
                    context.GetFilledWeeklyPayrollCents(business),
                    $"{stableTypes[i]} should retain enough cash for next required payroll. Blocked: {business.RuntimeState.LastWeeklyBlockedReason}");
                Assert.AreEqual(
                    0,
                    business.RuntimeState.LastSuspendedPayrollWorkerIds.Count,
                    $"{stableTypes[i]} should not routinely miss payroll. Blocked: {business.RuntimeState.LastWeeklyBlockedReason}");
            }
        }

        [Test]
        public void ButcherConvertsOneLivestockIntoMultipleMeatUnits()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState ranch = context.GetBusiness(BusinessType.Ranch);
            BusinessInstanceState butcher = context.GetBusiness(BusinessType.Butcher);
            context.SetBusinessStock(ranch, "livestock_inputs", 0);
            context.SetBusinessStock(butcher, "livestock_inputs", 1);
            context.SetBusinessStock(butcher, "meat", 0);
            context.SetBusinessCash(butcher, context.GetFilledWeeklyPayrollCents(butcher));

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.AreEqual(1, butcher.RuntimeState.LastWeeklyInputUnitsConsumed);
            Assert.AreEqual(8, butcher.RuntimeState.LastWeeklyOutputUnitsProduced);
        }

        [Test]
        public void LowCashBusinessesPreservePayrollReserveBeforeProcurementAndTransfers()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState ranch = context.GetBusiness(BusinessType.Ranch);
            BusinessInstanceState butcher = context.GetBusiness(BusinessType.Butcher);
            context.SetBusinessStock(ranch, "livestock_inputs", 20);
            context.SetBusinessStock(butcher, "livestock_inputs", 0);
            context.SetBusinessStock(butcher, "meat", 0);
            context.SetBusinessCash(butcher, context.GetFilledWeeklyPayrollCents(butcher) + 100);

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.AreEqual(0, butcher.RuntimeState.LastWeeklyLocalTransferCostCents);
            Assert.AreEqual(0, butcher.RuntimeState.LastWeeklyInputProcurementSpendCents);
            Assert.AreEqual(100, butcher.RuntimeState.CurrentCashCents);
            StringAssert.Contains("cash-limited local transfer", butcher.RuntimeState.LastWeeklyBlockedReason);
            StringAssert.Contains("cash-limited input procurement", butcher.RuntimeState.LastWeeklyBlockedReason);
        }

        [Test]
        public void MissedPayrollBlocksButPreservesWorkerSlotForRecovery()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState blacksmith = context.GetBusiness(BusinessType.Blacksmith);
            int filledBefore = blacksmith.RuntimeState.FilledWorkerCount;
            int payroll = context.GetFilledWeeklyPayrollCents(blacksmith);
            context.SetBusinessStock(blacksmith, "metal_inputs", 0);
            context.SetBusinessStock(blacksmith, "tools_hardware", 0);
            context.SetBusinessCash(blacksmith, 0);

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.AreEqual(filledBefore, blacksmith.RuntimeState.FilledWorkerCount);
            Assert.AreEqual(0, blacksmith.RuntimeState.ActiveWorkerCount);
            StringAssert.Contains("missed payroll", blacksmith.RuntimeState.LastWeeklyBlockedReason);

            context.SetBusinessCash(blacksmith, payroll);
            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.AreEqual(filledBefore, blacksmith.RuntimeState.FilledWorkerCount);
            Assert.Greater(blacksmith.RuntimeState.ActiveWorkerCount, 0);
            Assert.AreEqual(0, blacksmith.RuntimeState.LastSuspendedPayrollWorkerIds.Count);
        }

        [Test]
        public void GeneralStoreLocalSupplyIntakePreservesCashBuffer()
        {
            using TestWorld context = TestWorld.Create();
            int buffer = GeneralStoreRuntimeManager.MinimumPostReorderCashBufferForLocalSupplyCents;
            context.SetStoreStock("meat", 0);
            context.SetStoreCash(buffer);

            int accepted = context.StoreRuntime.ReceiveLocalSupply("meat", 1, 175, "Test Butcher", buffer);

            Assert.AreEqual(0, accepted);
            Assert.AreEqual(buffer, context.StoreRuntime.CurrentCashCents);
        }

        [Test]
        public void AgriculturalPlotsGenerateDressingAndWeeklyActivityCues()
        {
            using TestWorld context = TestWorld.Create();

            Assert.IsTrue(context.VisualNameContains("Agriculture Crop Field Row"), "Expected crop field row dressing.");
            Assert.IsTrue(context.VisualNameContains("Agriculture Ranch Fence"), "Expected ranch fence dressing.");

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.IsTrue(context.VisualNameContains("Agriculture Activity Cue"), "Expected weekly agricultural activity cue.");
        }

        [Test]
        public void GeneralStoreResourcesProfileMirrorsAuthoritativeRuntimeDefinition()
        {
            BusinessProfileDefinition authoritative = AssetDatabase.LoadAssetAtPath<BusinessProfileDefinition>(
                "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
            BusinessProfileDefinition mirror = AssetDatabase.LoadAssetAtPath<BusinessProfileDefinition>(
                "Assets/Resources/Core/Economy/BusinessProfiles/GeneralStoreProfile.asset");

            Assert.NotNull(authoritative);
            Assert.NotNull(mirror);
            Assert.AreEqual(JsonUtility.ToJson(authoritative), JsonUtility.ToJson(mirror));
        }

        [Test]
        public void HouseholdUpgradeOldSaveDefaultsToEmptyState()
        {
            GameObject gameObject = new("Household Upgrade Old Save Test");
            try
            {
                PopulationManager manager = gameObject.AddComponent<PopulationManager>();
                LandLedgers.Persistence.PopulationSaveDto dto = new();
                dto.households.Add(new LandLedgers.Persistence.HouseholdSaveDto
                {
                    id = 3,
                    householdName = "Hale Household",
                    surname = "Hale",
                    homeBuildingId = 12
                });

                manager.LoadFromSaveDto(dto);

                Assert.AreEqual(1, manager.State.households.Count);
                HouseholdState household = manager.State.households[0];
                Assert.NotNull(household.upgrades);
                Assert.AreEqual(0, household.upgrades.Count);
                Assert.AreEqual(0, household.foodReserveUnits);
                Assert.AreEqual(0, household.lastWeeklyUpgradeIncomeCents);
                Assert.AreEqual(0, household.lastWeeklyUpgradeUpkeepCents);
                Assert.AreEqual(0, household.lastWeeklyReserveDeltaUnits);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void HouseholdUpgradePersistenceRoundTripsBuiltUpgradesAndReserveState()
        {
            GameObject sourceObject = new("Household Upgrade Save Source Test");
            GameObject targetObject = new("Household Upgrade Save Target Test");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                HouseholdState household = new()
                {
                    id = 9,
                    householdName = "Fields Household",
                    surname = "Fields",
                    homeBuildingId = 21,
                    foodReserveUnits = 4,
                    lastWeeklyUpgradeIncomeCents = 50,
                    lastWeeklyUpgradeUpkeepCents = 25,
                    lastWeeklyReserveDeltaUnits = 2
                };
                household.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.ChickenCoop, 17));
                source.State.households.Add(household);

                LandLedgers.Persistence.PopulationSaveDto dto = source.CaptureSaveDto();
                target.LoadFromSaveDto(dto);

                HouseholdState restored = target.State.households[0];
                Assert.AreEqual(1, restored.upgrades.Count);
                Assert.AreEqual(HouseholdUpgradeKind.ChickenCoop, restored.upgrades[0].kind);
                Assert.IsTrue(restored.upgrades[0].built);
                Assert.AreEqual(17, restored.upgrades[0].builtDayIndex);
                Assert.AreEqual(4, restored.foodReserveUnits);
                Assert.AreEqual(50, restored.lastWeeklyUpgradeIncomeCents);
                Assert.AreEqual(25, restored.lastWeeklyUpgradeUpkeepCents);
                Assert.AreEqual(2, restored.lastWeeklyReserveDeltaUnits);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceObject);
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void HouseholdUpgradeEconomyAppliesReserveDemandAndWeeklyEffects()
        {
            HouseholdState household = new()
            {
                householdName = "Ward Household",
                spendingMoneyCents = 100,
                foodReserveUnits = 3
            };
            household.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.BoarderRoom, 1));

            HouseholdDemandSnapshot demand = HouseholdUpgradeEconomyEvaluator.ApplyDailyDemandEffects(
                household,
                new HouseholdDemandSnapshot { foodNeed = 2, generalGoodsNeed = 1 });

            Assert.AreEqual(2, demand.foodNeed);
            Assert.AreEqual(2, demand.generalGoodsNeed);
            Assert.AreEqual(2, household.foodReserveUnits);

            household.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.ChickenCoop, 2));
            household.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.KitchenGarden, 3));
            household.foodReserveUnits = 0;

            HouseholdUpgradeEconomyEvaluator.ApplyWeeklySettlementEffects(household);

            Assert.AreEqual(5, household.foodReserveUnits);
            Assert.AreEqual(400, household.lastWeeklyUpgradeIncomeCents);
            Assert.AreEqual(115, household.lastWeeklyUpgradeUpkeepCents);
            Assert.AreEqual(385, household.spendingMoneyCents);
            Assert.AreEqual(5, household.lastWeeklyReserveDeltaUnits);
        }

        [Test]
        public void HouseholdUpgradeConstructionConsumesInputsAndPersistsOnResidentHousehold()
        {
            using TestWorld context = TestWorld.Create();
            context.SetLumberYardStock(0);
            Assert.IsTrue(context.TryCreateOwnedResidentialHolding(out PlacedBuilding holding));
            HouseholdState household = context.SeedResidentHousehold(holding);
            context.SetBlacksmithHardwareStock(20);
            Assert.IsTrue(context.AcquisitionMarket.TryGetHouseholdUpgradeQuote(holding.id, 0, out ConstructionInputQuote quote, out string quoteMessage), quoteMessage);
            int lumberBefore = context.GetSawmillStock();
            int hardwareBefore = context.GetBlacksmithHardwareStock();
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;
            int storeCashBefore = context.StoreRuntime.CurrentCashCents;

            bool built = context.AcquisitionMarket.TryBuildHouseholdUpgrade(holding.id, 0, out HouseholdUpgradeState builtUpgrade, out string message);

            Assert.IsTrue(built, message);
            Assert.NotNull(builtUpgrade);
            Assert.IsTrue(HouseholdUpgradeCatalog.HasBuiltUpgrade(household, HouseholdUpgradeKind.ChickenCoop));
            Assert.AreEqual(lumberBefore - quote.GetLine(ConstructionResourceKind.Lumber).requiredUnits, context.GetSawmillStock());
            Assert.AreEqual(hardwareBefore - quote.GetLine(ConstructionResourceKind.Nails).requiredUnits, context.GetBlacksmithHardwareStock());
            Assert.AreEqual(ownerCashBefore - quote.CashCostCents, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(storeCashBefore, context.StoreRuntime.CurrentCashCents);
            StringAssert.Contains("Chicken Coop", message);
        }

        [Test]
        public void HouseholdUpgradeQuoteBlocksResidentialHoldingWithoutResidentHousehold()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryCreateOwnedResidentialHolding(out PlacedBuilding holding));

            bool quoted = context.AcquisitionMarket.TryGetHouseholdUpgradeQuote(holding.id, 0, out ConstructionInputQuote quote, out string message);

            Assert.IsFalse(quoted);
            Assert.IsNull(quote);
            StringAssert.Contains("No resident household assigned", message);
        }

        [Test]
        public void AcquisitionSaveRestoresSawmillSupportStateAndDefaultsOldSaves()
        {
            using TestWorld context = TestWorld.Create();
            context.SetSawmillStock(17);
            context.SetSawmillProductionState(23, 5, 4, 3, 2, 40, 1, true);
            context.SetBlacksmithHardwareStock(10);
            context.AcquisitionMarket.ResolveWeeklyConstructionSupportProduction();

            LandLedgers.Persistence.AcquisitionSaveDto dto = context.AcquisitionMarket.CaptureSaveDto();
            context.AcquisitionMarket.LoadFromSaveDto(dto);

            Assert.Greater(context.GetSawmillStock(), 17);
            Assert.Less(context.GetSawmillStandingTimber(), 23);
            Assert.GreaterOrEqual(context.GetSawmillLogs(), 0);
            Assert.Greater(context.GetSawmillNode().LastWeeklyLumberProducedUnits, 0);
            Assert.Greater(context.GetSawmillNode().LastWeeklySlabsOffcutsProducedUnits, 0);
            StringAssert.Contains("3 on-site worker houses", context.GetSawmillNode().LastWeeklyProductionSummary);

            LandLedgers.Persistence.AcquisitionSaveDto oldStyleDto = new()
            {
                marketSeed = 1902,
                maxLandListings = 4,
                maxBusinessListings = 2
            };
            context.AcquisitionMarket.LoadFromSaveDto(oldStyleDto);

            Assert.Greater(context.GetSawmillStock(), 0);
            Assert.Greater(context.GetSawmillStandingTimber(), 0);
            Assert.Greater(context.GetSawmillLogs(), 0);
        }

        [Test]
        public void SharedBusinessOldSaveBackfillsSmallMillAtSawmillYard()
        {
            using TestWorld context = TestWorld.Create();
            List<BusinessInstanceSaveDto> oldSharedDtos = new();
            List<BusinessInstanceSaveDto> currentDtos = context.SharedBusinessRuntime.CaptureSaveDtos();
            for (int i = 0; i < currentDtos.Count; i++)
            {
                if (currentDtos[i] != null && currentDtos[i].businessType != BusinessType.Sawmill)
                {
                    oldSharedDtos.Add(currentDtos[i]);
                }
            }

            context.SharedBusinessRuntime.LoadFromSaveDtos(oldSharedDtos);
            context.SharedBusinessRuntime.InitializeIfNeeded(context.StoreRuntime.CurrentBusiness);

            BusinessInstanceState sawmill = context.SharedBusinessRuntime.FindByType(BusinessType.Sawmill);
            Assert.NotNull(sawmill);
            TownPlot sawmillPlot = context.FindSawmillPlot();
            Assert.NotNull(sawmillPlot);
            Assert.AreEqual(sawmillPlot.buildingId, sawmill.AssignedBuildingId);
        }

        [Test]
        public void SharedBusinessSaveRoundTripPreservesSawmillCategoryStockAndSupportMirror()
        {
            using TestWorld source = TestWorld.Create();
            BusinessInstanceState sawmill = source.GetBusiness(BusinessType.Sawmill);
            source.SetSawmillStock(33);
            source.SetBusinessStock(sawmill, "standing_timber", 61);
            source.SetBusinessStock(sawmill, "sawmill_logs", 14);
            source.SetBusinessStock(sawmill, "slabs_offcuts", 9);
            source.SharedBusinessRuntime.SetSawmillConstructionSupportNode(source.GetSawmillNode());
            List<BusinessInstanceSaveDto> dtos = source.SharedBusinessRuntime.CaptureSaveDtos();

            using TestWorld restored = TestWorld.Create();
            restored.SharedBusinessRuntime.LoadFromSaveDtos(dtos);
            BusinessInstanceState restoredSawmill = restored.GetBusiness(BusinessType.Sawmill);
            Assert.AreEqual(61, restored.GetBusinessStock(restoredSawmill, "standing_timber"));
            Assert.AreEqual(14, restored.GetBusinessStock(restoredSawmill, "sawmill_logs"));
            Assert.AreEqual(33, restored.GetBusinessStock(restoredSawmill, "lumber"));
            Assert.AreEqual(9, restored.GetBusinessStock(restoredSawmill, "slabs_offcuts"));

            restored.SharedBusinessRuntime.SetSawmillConstructionSupportNode(restored.GetSawmillNode());
            Assert.AreEqual(61, restored.GetSawmillStandingTimber());
            Assert.AreEqual(14, restored.GetSawmillLogs());
            Assert.AreEqual(33, restored.GetSawmillStock());
        }

        [Test]
        public void MeaningfulDevelopmentJourneyRoundTripsThroughSaveDtos()
        {
            using TestWorld source = TestWorld.Create();
            source.SetLumberYardStock(0);
            source.SetBlacksmithHardwareStock(1000);
            Assert.IsTrue(source.TryFindBusinessShellOption(BusinessType.Blacksmith, out int plotId, out int businessIndex, out int shellIndex, out _, out _));
            Assert.IsTrue(source.AcquisitionMarket.TryConstructBusinessShellOnOwnedPlot(plotId, businessIndex, shellIndex, out PlacedBuilding shell, out string buildMessage), buildMessage);
            Assert.IsTrue(source.TryFindActivationOption(shell.id, out int activationIndex, out BusinessActivationCandidateState activationCandidate, out _));
            Assert.AreEqual(BusinessType.Blacksmith, activationCandidate.businessType);
            Assert.IsTrue(source.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, activationIndex, out BusinessInstanceState startedBusiness, out string startMessage), startMessage);
            source.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            GameObject sourceDebtObject = new("Slice Save Load Source Debt");
            GameObject restoredDebtObject = null;
            try
            {
                PlayerDebtManager sourceDebt = sourceDebtObject.AddComponent<PlayerDebtManager>();
                sourceDebt.Configure(null, source.StoreRuntime, source.AcquisitionMarket, source.SharedBusinessRuntime, source.PlayerPortfolio);
                sourceDebt.LoadFromSaveDto(new PlayerDebtSaveDto
                {
                    activeLoan = CreateIntegrationActiveLoan("slice_integration_save_load_loan", 25000, 1250)
                });

                Assert.IsTrue(sourceDebt.HasActiveLoan);
                int ownerCash = source.PlayerPortfolio.OwnerCashCents;
                int storeCash = source.StoreRuntime.CurrentCashCents;
                int ownedLandCount = source.AcquisitionMarket.OwnedLandCount;
                int ownedBusinessCount = source.AcquisitionMarket.OwnedBusinessCount;
                int sawmillStock = source.GetSawmillStock();
                int blacksmithStock = source.GetBlacksmithHardwareStock();
                int startedBusinessCash = startedBusiness.RuntimeState.CurrentCashCents;
                string startedBusinessId = startedBusiness.InstanceId;
                string activeDebtId = sourceDebt.ActiveLoan.loanId;
                int activeDebtPrincipal = sourceDebt.ActiveLoan.remainingPrincipalCents;
                int activeDebtPayment = sourceDebt.ActiveDebtPaymentCents;

                WorldSaveDto world = source.TownWorld.CaptureSaveDto();
                PopulationSaveDto population = source.PopulationManager.CaptureSaveDto();
                GeneralStoreSaveDto store = source.StoreRuntime.CaptureSaveDto();
                string deepStoreInstanceId = store.currentBusiness != null ? store.currentBusiness.instanceId : string.Empty;
                List<BusinessInstanceSaveDto> sharedBusinesses = source.SharedBusinessRuntime.CaptureSaveDtos(deepStoreInstanceId);
                List<LocalRecurringOrderRelationshipSaveDto> recurringOrders = source.SharedBusinessRuntime.CaptureLocalRecurringOrderRelationshipSaveDtos();
                string recurringOrderSummary = source.SharedBusinessRuntime.LastWeeklyRecurringLocalOrderSummary;
                PlayerPortfolioSaveDto portfolio = source.PlayerPortfolio.CaptureSaveDto();
                AcquisitionSaveDto acquisition = source.AcquisitionMarket.CaptureSaveDto();
                PlayerDebtSaveDto debt = sourceDebt.CaptureSaveDto();

                using TestWorld restored = TestWorld.Create();
                SaveReferenceResolver resolver = new(restored.TownWorld);
                Assert.IsTrue(restored.TownWorld.LoadFromSaveDto(world, resolver, out string worldMessage), worldMessage);
                restored.PopulationManager.LoadFromSaveDto(population);
                restored.StoreRuntime.LoadFromSaveDto(store);
                string restoredDeepStoreInstanceId = restored.StoreRuntime.CurrentBusiness != null
                    ? restored.StoreRuntime.CurrentBusiness.InstanceId
                    : string.Empty;
                restored.SharedBusinessRuntime.LoadFromSaveDtos(
                    sharedBusinesses,
                    restoredDeepStoreInstanceId,
                    recurringOrders,
                    recurringOrderSummary);
                restored.PlayerPortfolio.LoadFromSaveDto(portfolio);
                restored.AcquisitionMarket.LoadFromSaveDto(acquisition);

                restoredDebtObject = new GameObject("Slice Save Load Restored Debt");
                PlayerDebtManager restoredDebt = restoredDebtObject.AddComponent<PlayerDebtManager>();
                restoredDebt.Configure(null, restored.StoreRuntime, restored.AcquisitionMarket, restored.SharedBusinessRuntime, restored.PlayerPortfolio);
                restoredDebt.LoadFromSaveDto(debt);

                BusinessInstanceState restoredBusiness = restored.SharedBusinessRuntime.FindByBuildingId(shell.id);
                Assert.NotNull(restoredBusiness);
                Assert.AreEqual(startedBusinessId, restoredBusiness.InstanceId);
                Assert.AreEqual(BusinessOwnerKind.Player, restoredBusiness.Owner.OwnerKind);
                Assert.AreEqual(ownerCash, restored.PlayerPortfolio.OwnerCashCents);
                Assert.AreEqual(storeCash, restored.StoreRuntime.CurrentCashCents);
                Assert.AreEqual(ownedLandCount, restored.AcquisitionMarket.OwnedLandCount);
                Assert.AreEqual(ownedBusinessCount, restored.AcquisitionMarket.OwnedBusinessCount);
                Assert.AreEqual(sawmillStock, restored.GetSawmillStock());
                Assert.AreEqual(blacksmithStock, restored.GetBlacksmithHardwareStock());
                Assert.AreEqual(startedBusinessCash, restoredBusiness.RuntimeState.CurrentCashCents);
                Assert.IsTrue(restoredDebt.HasActiveLoan);
                Assert.NotNull(restoredDebt.ActiveLoan);
                Assert.AreEqual(activeDebtId, restoredDebt.ActiveLoan.loanId);
                Assert.AreEqual(activeDebtPrincipal, restoredDebt.ActiveLoan.remainingPrincipalCents);
                Assert.AreEqual(activeDebtPayment, restoredDebt.ActiveDebtPaymentCents);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceDebtObject);
                if (restoredDebtObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(restoredDebtObject);
                }
            }
        }

        [Test]
        public void MineRoundTripsThroughSharedBusinessSaveDtos()
        {
            using TestWorld source = TestWorld.Create();
            Assert.IsTrue(TryConstructShellForBusiness(source, BusinessType.Mine, out PlacedBuilding shell, out string buildMessage), buildMessage);
            Assert.IsTrue(TryFindActivationOption(source, shell.id, BusinessType.Mine, out int optionIndex, out _, out _));
            Assert.IsTrue(source.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, optionIndex, out BusinessInstanceState mine, out string startMessage), startMessage);
            source.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            string deepStoreInstanceId = source.StoreRuntime.CurrentBusiness != null
                ? source.StoreRuntime.CurrentBusiness.InstanceId
                : string.Empty;
            List<BusinessInstanceSaveDto> sharedBusinesses = source.SharedBusinessRuntime.CaptureSaveDtos(deepStoreInstanceId);

            using TestWorld restored = TestWorld.Create();
            restored.SharedBusinessRuntime.LoadFromSaveDtos(sharedBusinesses, deepStoreInstanceId);

            BusinessInstanceState restoredMine = restored.SharedBusinessRuntime.FindByBuildingId(shell.id);
            Assert.NotNull(restoredMine);
            Assert.NotNull(restoredMine.MineState);
            Assert.AreEqual(mine.MineState.MineralKind, restoredMine.MineState.MineralKind);
            Assert.AreEqual(mine.MineState.DepositConfidence01, restoredMine.MineState.DepositConfidence01);
            Assert.AreEqual(mine.MineState.DevelopmentStage, restoredMine.MineState.DevelopmentStage);
            Assert.AreEqual(mine.MineState.StockpileCategoryId, restoredMine.MineState.StockpileCategoryId);
            Assert.AreEqual(mine.MineState.LastWeeklyOutputSummary, restoredMine.MineState.LastWeeklyOutputSummary);
        }

        [Test]
        public void WeeklyMineOperationProducesStockAndOffMapRevenue()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(TryConstructShellForBusiness(context, BusinessType.Mine, out PlacedBuilding shell, out string buildMessage), buildMessage);
            Assert.IsTrue(TryFindActivationOption(context, shell.id, BusinessType.Mine, out int optionIndex, out _, out _));
            Assert.IsTrue(context.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, optionIndex, out BusinessInstanceState mine, out string startMessage), startMessage);

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            CategoryStockState stock = mine.RuntimeState.GetCategoryStock(mine.MineState.StockpileCategoryId);
            Assert.NotNull(stock);
            Assert.Greater(mine.MineState.LastWeeklyOutputUnits, 0);
            Assert.Greater(mine.RuntimeState.LastWeeklyOutputUnitsProduced, 0);
            Assert.Greater(mine.RuntimeState.LastWeeklyLocalTransferRevenueCents, 0);
            Assert.GreaterOrEqual(stock.CurrentStockUnits, 0);
            StringAssert.Contains("ore", mine.MineState.LastWeeklyOutputSummary.ToLowerInvariant());
        }

        [Test]
        public void MineWithoutPaidRequiredCrewDoesNotProduceOutput()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(TryConstructShellForBusiness(context, BusinessType.Mine, out PlacedBuilding shell, out string buildMessage), buildMessage);
            Assert.IsTrue(TryFindActivationOption(context, shell.id, BusinessType.Mine, out int optionIndex, out _, out _));
            Assert.IsTrue(context.AcquisitionMarket.TryStartBusinessFromOwnedShell(shell.id, optionIndex, out BusinessInstanceState mine, out string startMessage), startMessage);
            mine.RuntimeState.SetCurrentCashCents(0);

            context.SharedBusinessRuntime.ResolveWeeklySharedOperations();

            Assert.AreEqual(0, mine.RuntimeState.ActiveRequiredWorkerCount);
            Assert.AreEqual(0, mine.RuntimeState.LastWeeklyOutputUnitsProduced);
            Assert.AreEqual(0, mine.MineState.LastWeeklyOutputUnits);
            StringAssert.Contains("crew", mine.RuntimeState.LastWeeklyBlockedReason.ToLowerInvariant());
        }

        private static LoanContract CreateIntegrationActiveLoan(string loanId, int remainingPrincipalCents, int totalDueCents)
        {
            SimulationDate dueDate = CreateIntegrationLoanDate();
            return new LoanContract
            {
                loanId = loanId,
                lenderId = "frontier_local_bank",
                purpose = LoanPurpose.Acquisition,
                status = LoanStatus.Active,
                remainingPrincipalCents = remainingPrincipalCents,
                nextPaymentDueDate = dueDate,
                termStructure = new LoanTermStructure
                {
                    principalCents = remainingPrincipalCents,
                    annualInterestRateBps = 600,
                    termMonths = 12,
                    amortizationMonths = 12,
                    repaymentFrequency = RepaymentFrequency.Monthly
                },
                schedule = new LoanPaymentSchedule
                {
                    loanId = loanId,
                    principalCents = remainingPrincipalCents,
                    totalPrincipalCents = remainingPrincipalCents,
                    totalPaymentCents = totalDueCents,
                    payments = new List<LoanPaymentDue>
                    {
                        new()
                        {
                            paymentNumber = 1,
                            dueDate = dueDate,
                            dueDayIndex = 7,
                            principalCents = Mathf.Max(0, totalDueCents - 100),
                            interestCents = 100,
                            totalDueCents = totalDueCents,
                            status = LoanPaymentStatus.Scheduled
                        }
                    }
                }
            };
        }

        private static SimulationDate CreateIntegrationLoanDate()
        {
            return new SimulationDate(0, 0, 1, 1, 1, 1, 1);
        }

        private static bool TryConstructShellForBusiness(
            TestWorld context,
            BusinessType businessType,
            out PlacedBuilding shell,
            out string message)
        {
            shell = null;
            message = string.Empty;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                if (context.TryFindBusinessShellOption(
                        businessType,
                        out int plotId,
                        out int businessIndex,
                        out int shellIndex,
                        out _,
                        out _)
                    && context.AcquisitionMarket.TryConstructBusinessShellOnOwnedPlot(plotId, businessIndex, shellIndex, out shell, out message))
                {
                    return true;
                }

                if (!context.TryMarkNextBuildableEmptyPlotOwned(out _))
                {
                    break;
                }
            }

            message = string.IsNullOrWhiteSpace(message)
                ? $"No constructible {businessType} shell option was found."
                : message;
            return false;
        }

        private static bool TryFindActivationOption(
            TestWorld context,
            int buildingId,
            BusinessType businessType,
            out int optionIndex,
            out BusinessActivationCandidateState candidate,
            out ConstructionInputQuote quote)
        {
            optionIndex = -1;
            candidate = null;
            quote = null;
            int count = context.AcquisitionMarket.GetBusinessActivationCandidateCount(buildingId);
            for (int i = 0; i < count; i++)
            {
                if (!context.AcquisitionMarket.TryGetBusinessActivationCandidate(buildingId, i, out BusinessActivationCandidateState currentCandidate)
                    || currentCandidate == null
                    || currentCandidate.businessType != businessType
                    || !currentCandidate.eligible)
                {
                    continue;
                }

                if (context.AcquisitionMarket.TryGetBusinessActivationQuote(buildingId, i, out ConstructionInputQuote currentQuote, out _)
                    && currentQuote != null
                    && currentQuote.CanProceed)
                {
                    optionIndex = i;
                    candidate = currentCandidate;
                    quote = currentQuote;
                    return true;
                }
            }

            return false;
        }

        private sealed class TestWorld : IDisposable
        {
            private readonly GameObject root;
            private readonly TownGenerationSettings settings;

            public TownWorldController TownWorld { get; }
            public PopulationManager PopulationManager { get; }
            public GeneralStoreRuntimeManager StoreRuntime { get; }
            public SharedBusinessRuntimeManager SharedBusinessRuntime { get; }
            public AcquisitionMarketManager AcquisitionMarket { get; }
            public PlayerPortfolioManager PlayerPortfolio { get; }
            public TownGenerationSettings Settings => settings;

            private TestWorld(GameObject root, TownGenerationSettings settings)
            {
                this.root = root;
                this.settings = settings;
                TownWorld = root.AddComponent<TownWorldController>();
                PopulationManager = root.AddComponent<PopulationManager>();
                StoreRuntime = root.AddComponent<GeneralStoreRuntimeManager>();
                SharedBusinessRuntime = root.AddComponent<SharedBusinessRuntimeManager>();
                AcquisitionMarket = root.AddComponent<AcquisitionMarketManager>();
                PlayerPortfolio = root.AddComponent<PlayerPortfolioManager>();
            }

            public static TestWorld Create()
            {
                TownGenerationSettings sourceSettings = AssetDatabase.LoadAssetAtPath<TownGenerationSettings>("Assets/Core/World/DefaultTownGenerationSettings.asset");
                GeneralStoreBusinessDefinition storeDefinition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>("Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
                Assert.NotNull(sourceSettings);
                Assert.NotNull(storeDefinition);

                TownGenerationSettings settings = UnityEngine.Object.Instantiate(sourceSettings);
                GameObject root = new("Construction Input Support Test World");
                TestWorld context = new(root, settings);
                context.TownWorld.Configure(settings, null, null);
                context.TownWorld.GenerateTownShell();
                context.SeedLaborPool(24);
                context.PlayerPortfolio.Configure(null, null);
                context.StoreRuntime.Configure(context.TownWorld, context.PopulationManager, null, storeDefinition, null, null, context.PlayerPortfolio);
                Assert.IsTrue(context.StoreRuntime.InitializeIfNeeded(), context.StoreRuntime.Status);
                context.SharedBusinessRuntime.Configure(context.TownWorld, null, context.PopulationManager, null, context.StoreRuntime, context.PlayerPortfolio);
                context.SharedBusinessRuntime.InitializeIfNeeded(context.StoreRuntime.CurrentBusiness);
                context.PlayerPortfolio.InitializeFromLegacyBusinessCash(context.StoreRuntime, context.SharedBusinessRuntime);
                context.PlayerPortfolio.AddOwnerCash(TestOwnerConstructionCashCents, "construction test working capital");
                context.AcquisitionMarket.Configure(context.TownWorld, context.StoreRuntime, context.SharedBusinessRuntime, context.PopulationManager, null, null, context.PlayerPortfolio);
                context.AcquisitionMarket.RebuildMarket();
                context.SetLumberYardStock(0);
                context.MarkFirstBuildableEmptyPlotOwned();
                return context;
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

            public bool TryFindBuildableOption(out int plotId, out int optionIndex, out ConstructionInputQuote quote)
            {
                plotId = -1;
                optionIndex = -1;
                quote = null;
                IReadOnlyList<OwnedPlotBuildabilityState> states = AcquisitionMarket.GetOwnedBuildablePlots();
                for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
                {
                    OwnedPlotBuildabilityState state = states[stateIndex];
                    if (state == null || !state.buildable || state.buildOptions == null)
                    {
                        continue;
                    }

                    for (int buildIndex = 0; buildIndex < state.buildOptions.Count; buildIndex++)
                    {
                        if (AcquisitionMarket.TryGetConstructionQuoteForOwnedPlotOption(state.plotId, buildIndex, out ConstructionInputQuote candidate, out _)
                            && candidate != null
                            && candidate.CanProceed)
                        {
                            plotId = state.plotId;
                            optionIndex = buildIndex;
                            quote = candidate;
                            return true;
                        }
                    }
                }

                return false;
            }

            public bool TryFindAdditionalBuildableOption(int excludedPlotId, out int plotId, out int optionIndex)
            {
                plotId = -1;
                optionIndex = -1;
                for (int i = 0; i < TownWorld.Plots.Count; i++)
                {
                    TownPlot plot = TownWorld.Plots[i];
                    if (plot == null || plot.id == excludedPlotId || plot.buildingId >= 0)
                    {
                        continue;
                    }

                    bool wasOwned = plot.playerOwned;
                    plot.playerOwned = true;
                    AcquisitionMarket.RebuildMarket();

                    IReadOnlyList<OwnedPlotBuildabilityState> states = AcquisitionMarket.GetOwnedBuildablePlots();
                    for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
                    {
                        OwnedPlotBuildabilityState state = states[stateIndex];
                        if (state == null || state.plotId != plot.id || !state.buildable || state.buildOptions == null)
                        {
                            continue;
                        }

                        for (int buildIndex = 0; buildIndex < state.buildOptions.Count; buildIndex++)
                        {
                            if (AcquisitionMarket.TryGetConstructionQuoteForOwnedPlotOption(state.plotId, buildIndex, out ConstructionInputQuote quote, out _)
                                && quote != null
                                && quote.CanProceed)
                            {
                                plotId = state.plotId;
                                optionIndex = buildIndex;
                                return true;
                            }
                        }
                    }

                    plot.playerOwned = wasOwned;
                }

                AcquisitionMarket.RebuildMarket();
                return false;
            }

            public bool TryFindOffRecommendationBuildOption(out int plotId, out int optionIndex, out PlayerDevelopmentFitResult fit)
            {
                plotId = -1;
                optionIndex = -1;
                fit = null;
                IReadOnlyList<OwnedPlotBuildabilityState> states = AcquisitionMarket.GetOwnedBuildablePlots();
                for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
                {
                    OwnedPlotBuildabilityState state = states[stateIndex];
                    if (state == null || !state.buildable || state.buildOptions == null)
                    {
                        continue;
                    }

                    for (int buildIndex = 0; buildIndex < state.buildOptions.Count; buildIndex++)
                    {
                        BuildingDefinition definition = state.buildOptions[buildIndex];
                        if (definition == null || definition.CanUsePlot(state.plotZone))
                        {
                            continue;
                        }

                        if (AcquisitionMarket.TryGetConstructionFitForOwnedPlotOption(state.plotId, buildIndex, out PlayerDevelopmentFitResult candidateFit)
                            && AcquisitionMarket.TryGetConstructionQuoteForOwnedPlotOption(state.plotId, buildIndex, out ConstructionInputQuote quote, out _)
                            && quote != null
                            && quote.CanProceed)
                        {
                            plotId = state.plotId;
                            optionIndex = buildIndex;
                            fit = candidateFit;
                            return true;
                        }
                    }
                }

                return false;
            }

            public bool TryFindBusinessDevelopmentCandidateIndex(BusinessType businessType, out int optionIndex)
            {
                optionIndex = -1;
                int count = AcquisitionMarket.GetBusinessDevelopmentCandidateCount();
                for (int i = 0; i < count; i++)
                {
                    if (AcquisitionMarket.TryGetBusinessDevelopmentCandidate(i, out BusinessActivationCandidateState candidate)
                        && candidate != null
                        && candidate.businessType == businessType)
                    {
                        optionIndex = i;
                        return true;
                    }
                }

                return false;
            }

            public bool TryFindBusinessShellOption(
                BusinessType businessType,
                out int plotId,
                out int businessIndex,
                out int shellIndex,
                out BusinessActivationCandidateState candidate,
                out ConstructionInputQuote quote)
            {
                return TryFindBusinessShellQuote(
                    businessType,
                    true,
                    out plotId,
                    out businessIndex,
                    out shellIndex,
                    out candidate,
                    out quote,
                    out _);
            }

            public bool TryFindBusinessShellQuote(
                BusinessType businessType,
                bool requireCanProceed,
                out int plotId,
                out int businessIndex,
                out int shellIndex,
                out BusinessActivationCandidateState candidate,
                out ConstructionInputQuote quote,
                out string message)
            {
                plotId = -1;
                businessIndex = -1;
                shellIndex = -1;
                candidate = null;
                quote = null;
                message = string.Empty;
                if (!TryFindBusinessDevelopmentCandidateIndex(businessType, out businessIndex)
                    || !AcquisitionMarket.TryGetBusinessDevelopmentCandidate(businessIndex, out candidate)
                    || candidate == null
                    || !candidate.eligible)
                {
                    message = candidate != null ? candidate.reason : $"No {businessType} development candidate.";
                    return false;
                }

                IReadOnlyList<OwnedPlotBuildabilityState> states = AcquisitionMarket.GetOwnedBuildablePlots();
                for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
                {
                    OwnedPlotBuildabilityState state = states[stateIndex];
                    if (state == null || !state.buildable)
                    {
                        continue;
                    }

                    IReadOnlyList<BuildingDefinition> options = AcquisitionMarket.GetBusinessBuildOptionsForOwnedPlot(state.plotId, businessIndex);
                    for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
                    {
                        if (AcquisitionMarket.TryGetBusinessConstructionQuoteForOwnedPlotOption(state.plotId, businessIndex, optionIndex, out ConstructionInputQuote currentQuote, out string currentMessage)
                            && currentQuote != null
                            && (!requireCanProceed || currentQuote.CanProceed))
                        {
                            plotId = state.plotId;
                            shellIndex = optionIndex;
                            quote = currentQuote;
                            message = currentMessage;
                            return true;
                        }

                        if (!string.IsNullOrWhiteSpace(currentMessage))
                        {
                            message = currentMessage;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    message = $"No shell quote found for {businessType}.";
                }

                return false;
            }

            public OwnedPlotBuildabilityState FindBuildabilityState(int plotId)
            {
                IReadOnlyList<OwnedPlotBuildabilityState> states = AcquisitionMarket.GetOwnedBuildablePlots();
                for (int i = 0; i < states.Count; i++)
                {
                    if (states[i] != null && states[i].plotId == plotId)
                    {
                        return states[i];
                    }
                }

                return null;
            }

            public bool TryBuildShell(out PlacedBuilding shell)
            {
                shell = null;
                if (!TryFindBuildableOption(out int plotId, out int optionIndex, out _))
                {
                    return false;
                }

                return AcquisitionMarket.TryConstructOnOwnedPlot(plotId, optionIndex, out shell, out _);
            }

            public TownPlot GetPlot(int plotId)
            {
                return ResolvePlot(plotId);
            }

            public bool TryFindActivationOption(
                int buildingId,
                out int optionIndex,
                out BusinessActivationCandidateState candidate,
                out ConstructionInputQuote quote)
            {
                optionIndex = -1;
                candidate = null;
                quote = null;
                int count = AcquisitionMarket.GetBusinessActivationCandidateCount(buildingId);
                for (int i = 0; i < count; i++)
                {
                    if (!AcquisitionMarket.TryGetBusinessActivationCandidate(buildingId, i, out BusinessActivationCandidateState currentCandidate)
                        || currentCandidate == null
                        || !currentCandidate.eligible)
                    {
                        continue;
                    }

                    if (AcquisitionMarket.TryGetBusinessActivationQuote(buildingId, i, out ConstructionInputQuote currentQuote, out _)
                        && currentQuote != null
                        && currentQuote.CanProceed)
                    {
                        optionIndex = i;
                        candidate = currentCandidate;
                        quote = currentQuote;
                        return true;
                    }
                }

                return false;
            }

            public bool TryFindActivationCandidateIndex(int buildingId, BusinessType businessType, out int optionIndex)
            {
                optionIndex = -1;
                int count = AcquisitionMarket.GetBusinessActivationCandidateCount(buildingId);
                for (int i = 0; i < count; i++)
                {
                    if (AcquisitionMarket.TryGetBusinessActivationCandidate(buildingId, i, out BusinessActivationCandidateState candidate)
                        && candidate != null
                        && candidate.businessType == businessType)
                    {
                        optionIndex = i;
                        return true;
                    }
                }

                return false;
            }

            public bool TryCreateOwnedResidentialHolding(out PlacedBuilding holding)
            {
                holding = null;
                BuildingDefinition house = AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Core/World/Buildings/HouseNaturalWood.asset");
                Assert.NotNull(house);

                for (int i = 0; i < TownWorld.Buildings.Count; i++)
                {
                    PlacedBuilding building = TownWorld.Buildings[i];
                    if (building == null || building.definition == null || building.definition.CanHostWorkplace)
                    {
                        continue;
                    }

                    building.playerOwned = true;
                    TownPlot plot = ResolvePlot(building.plotId);
                    if (plot != null)
                    {
                        plot.playerOwned = true;
                    }

                    AcquisitionMarket.RebuildMarket();
                    holding = building;
                    return true;
                }

                for (int i = 0; i < TownWorld.Plots.Count; i++)
                {
                    TownPlot plot = TownWorld.Plots[i];
                    if (plot == null || plot.zone != PlotZone.Residential || plot.buildingId >= 0)
                    {
                        continue;
                    }

                    plot.playerOwned = true;
                    if (TownWorld.TryConstructBuildingShell(plot.id, house, true, out holding, out _))
                    {
                        AcquisitionMarket.RebuildMarket();
                        return true;
                    }

                    plot.playerOwned = false;
                }

                return false;
            }

            public HouseholdState SeedResidentHousehold(PlacedBuilding home)
            {
                Assert.NotNull(home);
                int householdId = PopulationManager.State.households.Count;
                int personId = PopulationManager.State.people.Count;
                HouseholdState household = new()
                {
                    id = householdId,
                    householdName = "Resident Household",
                    surname = "Resident",
                    homeBuildingId = home.id,
                    weeklyIncomeSnapshot = 12,
                    spendingMoneyCents = 1200,
                    demandSnapshot = HouseholdDemandSnapshot.Empty()
                };
                household.memberIds.Add(personId);

                PopulationManager.State.people.Add(new PersonState
                {
                    id = personId,
                    firstName = "Ruth",
                    lastName = "Resident",
                    age = 30,
                    ageBand = AgeBand.Adult18Plus,
                    laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                    householdId = householdId,
                    professionId = string.Empty,
                    professionName = "No assigned job",
                    wage = WageSnapshot.None(),
                    homeBuildingId = home.id,
                    workplaceBuildingId = -1,
                    scheduleState = PopulationScheduleState.AtHome,
                    currentDestinationBuildingId = home.id
                });

                PopulationManager.State.households.Add(household);
                return household;
            }

            public void SetSawmillStock(int units)
            {
                int stockUnits = Mathf.Max(0, units);
                GetSawmillNode().SetStockForTests(stockUnits);
                BusinessInstanceState sawmill = SharedBusinessRuntime.FindByType(BusinessType.Sawmill);
                if (sawmill != null && sawmill.RuntimeState != null)
                {
                    SetBusinessStock(sawmill, "lumber", stockUnits);
                }
            }

            public int GetSawmillStock()
            {
                return GetSawmillNode().CurrentStockUnits;
            }

            public void SetLumberYardStock(int units)
            {
                BusinessInstanceState yard = SharedBusinessRuntime.FindByType(BusinessType.LumberYard);
                if (yard != null && yard.RuntimeState != null)
                {
                    SetBusinessStock(yard, "lumber", Mathf.Max(0, units));
                }
            }

            public int GetLumberYardStock()
            {
                BusinessInstanceState yard = SharedBusinessRuntime.FindByType(BusinessType.LumberYard);
                CategoryStockState stock = yard != null && yard.RuntimeState != null
                    ? yard.RuntimeState.GetCategoryStock("lumber")
                    : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }

            public int GetSawmillStandingTimber()
            {
                return GetSawmillNode().StandingTimberUnits;
            }

            public int GetSawmillLogs()
            {
                return GetSawmillNode().LogStockUnits;
            }

            public void SetSawmillProductionState(
                int standingTimber,
                int logs,
                int cutCapacity,
                int sawCapacity,
                int lumberPerLog,
                int storageCapacity,
                int hardwareMaintenanceUnits,
                bool workerCabins)
            {
                GetSawmillNode().SetSawmillProductionForTests(
                    standingTimber,
                    logs,
                    cutCapacity,
                    sawCapacity,
                    lumberPerLog,
                    storageCapacity,
                    hardwareMaintenanceUnits,
                    workerCabins);

                BusinessInstanceState sawmill = SharedBusinessRuntime.FindByType(BusinessType.Sawmill);
                if (sawmill != null && sawmill.RuntimeState != null)
                {
                    SetBusinessStock(sawmill, "standing_timber", Mathf.Max(0, standingTimber));
                    SetBusinessStock(sawmill, "sawmill_logs", Mathf.Max(0, logs));
                }
            }

            public void SetSawmillHauling(bool ownedInternalHauling, bool hiredFallbackAllowed, int hiredCostPerLogCents)
            {
                GetSawmillNode().SetSawmillHaulingForTests(ownedInternalHauling, hiredFallbackAllowed, hiredCostPerLogCents);
            }

            public ConstructionSupportNodeState GetSawmillNode()
            {
                for (int i = 0; i < AcquisitionMarket.ConstructionSupportNodes.Count; i++)
                {
                    ConstructionSupportNodeState node = AcquisitionMarket.ConstructionSupportNodes[i];
                    if (node != null && node.ResourceKind == ConstructionResourceKind.Lumber)
                    {
                        return node;
                    }
                }

                Assert.Fail("No lumber support node found.");
                return null;
            }

            public TownPlot FindSawmillPlot()
            {
                for (int i = 0; i < TownWorld.Plots.Count; i++)
                {
                    TownPlot plot = TownWorld.Plots[i];
                    if (plot != null && plot.agriculturalSiteRole == AgriculturalSiteRole.SawmillYard)
                    {
                        return plot;
                    }
                }

                return null;
            }

            public void SetBlacksmithHardwareStock(int units)
            {
                CategoryStockState stock = GetBlacksmithHardwareStockState();
                Assert.NotNull(stock);
                stock.SetCurrentStockForTests(units);
            }

            public int GetBlacksmithHardwareStock()
            {
                CategoryStockState stock = GetBlacksmithHardwareStockState();
                return stock != null ? stock.CurrentStockUnits : 0;
            }

            public void SetStoreStock(string categoryId, int units)
            {
                CategoryStockState stock = StoreRuntime.RuntimeState.GetCategoryStock(categoryId);
                Assert.NotNull(stock, $"Store category {categoryId} should exist.");
                stock.SetCurrentStockForTests(units);
            }

            public void SetStoreStockToTarget(string categoryId)
            {
                CategoryStockState stock = StoreRuntime.RuntimeState.GetCategoryStock(categoryId);
                Assert.NotNull(stock, $"Store category {categoryId} should exist.");
                stock.SetCurrentStockForTests(stock.TargetStockUnits);
            }

            public int GetStoreStock(string categoryId)
            {
                CategoryStockState stock = StoreRuntime.RuntimeState.GetCategoryStock(categoryId);
                return stock != null ? stock.CurrentStockUnits : 0;
            }

            public BusinessInstanceState GetBusiness(BusinessType businessType)
            {
                BusinessInstanceState business = SharedBusinessRuntime.FindByType(businessType);
                Assert.NotNull(business, $"Expected shared {businessType} business in the default town.");
                Assert.NotNull(business.RuntimeState, $"Expected shared {businessType} runtime state.");
                return business;
            }

            public void SetBusinessStock(BusinessInstanceState business, string categoryId, int units)
            {
                Assert.NotNull(business);
                CategoryStockState stock = business.RuntimeState.GetCategoryStock(categoryId);
                Assert.NotNull(stock, $"{business.BusinessType} category {categoryId} should exist.");
                stock.SetCurrentStockForTests(units);
            }

            public int GetBusinessStock(BusinessInstanceState business, string categoryId)
            {
                Assert.NotNull(business);
                CategoryStockState stock = business.RuntimeState.GetCategoryStock(categoryId);
                Assert.NotNull(stock, $"{business.BusinessType} category {categoryId} should exist.");
                return stock.CurrentStockUnits;
            }

            public void SetWorkerSlotFilled(BusinessInstanceState business, string slotId, bool filled)
            {
                Assert.NotNull(business);
                Assert.IsFalse(string.IsNullOrWhiteSpace(slotId));
                for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
                {
                    WorkerSlotState slot = business.RuntimeState.WorkerSlots[i];
                    if (!string.Equals(slot.SlotId, slotId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (filled)
                    {
                        slot.Assign($"{business.InstanceId}_{slotId}_test", slot.SlotDisplayName, slot.WeeklyWageCents);
                        slot.MarkPaidActive();
                    }
                    else
                    {
                        slot.Unassign();
                    }

                    return;
                }

                Assert.Fail($"Could not find worker slot '{slotId}' on {business.BusinessType}.");
            }

            public void SetBusinessCash(BusinessInstanceState business, int cashCents)
            {
                Assert.NotNull(business);
                int target = Mathf.Max(0, cashCents);
                int current = business.RuntimeState.CurrentCashCents;
                if (current > target)
                {
                    business.RuntimeState.SpendCents(current - target);
                    return;
                }

                business.RuntimeState.AddCashCents(target - current);
            }

            public void SetStoreCash(int cashCents)
            {
                int target = Mathf.Max(0, cashCents);
                int current = StoreRuntime.RuntimeState.CurrentCashCents;
                if (current > target)
                {
                    StoreRuntime.RuntimeState.SpendCents(current - target);
                    return;
                }

                StoreRuntime.RuntimeState.AddCashCents(target - current);
            }

            public int GetFilledWeeklyPayrollCents(BusinessInstanceState business)
            {
                Assert.NotNull(business);
                int payroll = 0;
                for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
                {
                    WorkerSlotState slot = business.RuntimeState.WorkerSlots[i];
                    if (slot.IsFilled)
                    {
                        payroll += slot.WeeklyWageCents;
                    }
                }

                return payroll;
            }

            public bool VisualNameContains(string text)
            {
                return VisualNameContains(TownWorld.VisualRoot, text);
            }

            private CategoryStockState GetBlacksmithHardwareStockState()
            {
                BusinessInstanceState blacksmith = SharedBusinessRuntime.FindByType(BusinessType.Blacksmith);
                Assert.NotNull(blacksmith, "Expected a shared blacksmith business in the default town.");
                return blacksmith.RuntimeState.GetCategoryStock("tools_hardware");
            }

            private static bool VisualNameContains(Transform root, string text)
            {
                if (root == null)
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(root.name) && root.name.Contains(text))
                {
                    return true;
                }

                for (int i = 0; i < root.childCount; i++)
                {
                    if (VisualNameContains(root.GetChild(i), text))
                    {
                        return true;
                    }
                }

                return false;
            }

            public TownPlot GetPlotById(int plotId)
            {
                return ResolvePlot(plotId);
            }

            public bool TryMarkNextBuildableEmptyPlotOwned(out int plotId)
            {
                plotId = -1;
                for (int i = 0; i < TownWorld.Plots.Count; i++)
                {
                    TownPlot plot = TownWorld.Plots[i];
                    if (plot == null || plot.playerOwned || plot.buildingId >= 0)
                    {
                        continue;
                    }

                    plot.playerOwned = true;
                    AcquisitionMarket.RebuildMarket();
                    OwnedPlotBuildabilityState state = FindBuildabilityState(plot.id);
                    if (state != null && state.buildable)
                    {
                        plotId = plot.id;
                        return true;
                    }

                    plot.playerOwned = false;
                    AcquisitionMarket.RebuildMarket();
                }

                return false;
            }

            private TownPlot ResolvePlot(int plotId)
            {
                for (int i = 0; i < TownWorld.Plots.Count; i++)
                {
                    TownPlot plot = TownWorld.Plots[i];
                    if (plot != null && plot.id == plotId)
                    {
                        return plot;
                    }
                }

                return null;
            }

            public void SeedLaborPool(int count)
            {
                PopulationManager.State.people.Clear();
                for (int i = 0; i < count; i++)
                {
                    PopulationManager.State.people.Add(new PersonState
                    {
                        id = i,
                        firstName = "Worker",
                        lastName = i.ToString("00"),
                        age = 22,
                        ageBand = AgeBand.Adult18Plus,
                        laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                        householdId = -1,
                        professionId = string.Empty,
                        professionName = "No assigned job",
                        homeBuildingId = -1,
                        workplaceBuildingId = -1
                    });
                }
            }

            private void MarkFirstBuildableEmptyPlotOwned()
            {
                for (int i = 0; i < TownWorld.Plots.Count; i++)
                {
                    TownPlot plot = TownWorld.Plots[i];
                    if (plot == null || plot.buildingId >= 0)
                    {
                        continue;
                    }

                    plot.playerOwned = true;
                    AcquisitionMarket.RebuildMarket();
                    if (TryFindBuildableOption(out _, out _, out _))
                    {
                        return;
                    }

                    plot.playerOwned = false;
                }

                Assert.Fail("No buildable empty plot found in the generated town.");
            }
        }


        [Test]
        public void ConstructionSupportSummariesDescribeRemoteProductionAndBuilderReadiness()
        {
            ConstructionSupportNodeDefinition definition = ScriptableObject.CreateInstance<ConstructionSupportNodeDefinition>();
            try
            {
                string remoteSummary = definition.BuildRemoteProductionSummary();
                string tradeSummary = definition.BuildConstructionTradeReadSummary();
                string projectSummary = definition.BuildProjectReadinessSummary(2, 4, 1);
                TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
                settings.Sanitize();
                string builderSummary = definition.BuildBuilderAvailabilitySummary(settings);
                string projectSummaryWithSettings = definition.BuildProjectReadinessSummary(settings, true);

                StringAssert.Contains("Local Sawmill", remoteSummary);
                StringAssert.Contains("Typical supply path", tradeSummary);
                StringAssert.Contains("house ~2 week", projectSummary);
                StringAssert.Contains("business ~4 week", projectSummary);
                StringAssert.Contains("carpenter", builderSummary.ToLowerInvariant());
                StringAssert.Contains("Business build", projectSummaryWithSettings);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }



        [Test]
        public void TownGenerationSettings_ProjectQueueAndClimateHelpersReportUsefulBuildReads()
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            try
            {
                settings.Sanitize();
                int queuedWeeks = settings.EstimateQueuedBuildWeeks(definition, 2, true);
                string queueSummary = settings.BuildProjectQueueSummary(definition, 2, true);
                string climate = settings.BuildConstructionClimateHeadline(true, 2);

                Assert.GreaterOrEqual(queuedWeeks, 1);
                StringAssert.Contains("Queue ahead: 2", queueSummary);
                StringAssert.Contains("Material flow looks tight", queueSummary);
                StringAssert.Contains("Local mill support", climate);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void ConstructionSupportState_WeeklyReviewFlagsMaterialTightProjects()
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            ConstructionSupportNodeDefinition definition = ScriptableObject.CreateInstance<ConstructionSupportNodeDefinition>();
            BuildingDefinition buildingDefinition = ScriptableObject.CreateInstance<BuildingDefinition>();
            try
            {
                settings.Sanitize();
                ConstructionSupportNodeState state = ConstructionSupportNodeState.FromDefinition(definition);
                state.SetStockForTests(2);
                bool ready = state.IsProjectLikelyStartReady(settings, 8, 1, out string reason);
                string review = state.BuildWeeklyConstructionReviewSummary(settings, buildingDefinition, 1, 8);

                Assert.IsFalse(ready);
                StringAssert.Contains("Material-tight", reason);
                StringAssert.Contains("Project stance: delayed", review);
                StringAssert.Contains("Queue ahead: 1", review);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(buildingDefinition);
            }
        }

        [Test]
        public void TownGenerationSettings_InputEstimateSummaryReportsLumberAndLabor()
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            try
            {
                settings.Sanitize();
                string inputRead = settings.BuildProjectInputEstimateSummary(definition);
                int lumber = settings.EstimateTypicalLumberUnitsForDefinition(definition);
                int labor = settings.EstimateTypicalLaborUnitsForDefinition(definition);

                Assert.Greater(lumber, 0);
                Assert.Greater(labor, 0);
                StringAssert.Contains("lumber", inputRead.ToLowerInvariant());
                StringAssert.Contains("labor", inputRead.ToLowerInvariant());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void ConstructionSupportState_ProjectStartAuthorityFlagsHighRiskDelay()
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            ConstructionSupportNodeDefinition definition = ScriptableObject.CreateInstance<ConstructionSupportNodeDefinition>();
            BuildingDefinition buildingDefinition = ScriptableObject.CreateInstance<BuildingDefinition>();
            try
            {
                settings.guaranteeLocalCarpenter = false;
                settings.Sanitize();
                ConstructionSupportNodeState state = ConstructionSupportNodeState.FromDefinition(definition);
                state.SetStockForTests(1);
                state.RecordSawmillWeeklyResult(0, 0, 0, "No production.", "Hardware shortfall");

                string authority = state.BuildProjectStartAuthoritySummary(settings, buildingDefinition, 2, 12);
                int score = state.EvaluateProjectConstraintScore(settings, buildingDefinition, 2, 12);

                Assert.GreaterOrEqual(score, 5);
                StringAssert.Contains("high-risk delay", authority.ToLowerInvariant());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(buildingDefinition);
            }
        }

        [Test]
        public void ConstructionSupportState_WeeklyProjectReviewPersistsBlockedAndReadyStreaks()
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            settings.guaranteeLocalCarpenter = false;
            settings.minimumLocalCarpenterCount = 0;

            BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            definition.ConfigureRuntimeFallback(
                "weekly_project_review_business_shell",
                "Weekly Project Review Business Shell",
                PlotZone.Business,
                new Vector2Int(8, 6),
                Color.white,
                5f);

            try
            {
                ConstructionSupportNodeState state = ConstructionSupportNodeState.CreateFallbackSawmill();
                state.TryConsume(Mathf.Max(0, state.CurrentStockUnits - 10));
                string blocked = state.ApplyWeeklyProjectReview(settings, definition, 2, 60);
                StringAssert.Contains("Constraint score", blocked);
                Assert.GreaterOrEqual(state.BlockedProjectReviewCount, 1);
                Assert.AreEqual(0, state.ReadyProjectReviewCount);

                ConstructionSupportNodeSaveDto dto = state.CaptureSaveDto();
                Assert.GreaterOrEqual(dto.blockedProjectReviewCount, 1);
                Assert.GreaterOrEqual(dto.lastProjectConstraintScore, 1);
                StringAssert.Contains("Constraint score", dto.lastWeeklyProjectReviewSummary);

                ConstructionSupportNodeState restored = ConstructionSupportNodeState.FromSaveDto(dto);
                Assert.AreEqual(state.BlockedProjectReviewCount, restored.BlockedProjectReviewCount);
                Assert.AreEqual(state.LastProjectConstraintScore, restored.LastProjectConstraintScore);

                settings.guaranteeLocalCarpenter = true;
                restored.AddStock(200, false);
                string ready = restored.ApplyWeeklyProjectReview(settings, definition, 0, 20);
                StringAssert.Contains("Constraint score", ready);
                Assert.GreaterOrEqual(restored.ReadyProjectReviewCount, 1);
                Assert.AreEqual(0, restored.BlockedProjectReviewCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void QueuedShellConstructionProgressesAcrossWeeksAndCompletes()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out _));

            bool queued = context.AcquisitionMarket.TryQueueConstructOnOwnedPlot(plotId, optionIndex, out string queueMessage);

            Assert.IsTrue(queued, queueMessage);
            Assert.AreEqual(1, context.AcquisitionMarket.ActiveConstructionProjects.Count);
            StringAssert.Contains("Queued shell", queueMessage);

            for (int week = 0; week < 8 && context.GetPlot(plotId).buildingId < 0; week++)
            {
                context.AcquisitionMarket.ResolveWeeklyConstructionQueue(out _);
            }

            Assert.GreaterOrEqual(context.GetPlot(plotId).buildingId, 0);
            Assert.AreEqual(0, context.AcquisitionMarket.ActiveConstructionProjects.Count);
        }

        [Test]
        public void QueuedShellConstructionStallsWhenInputsAreThinThenRecovers()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out _));
            context.SetSawmillStock(0);
            context.SetBlacksmithHardwareStock(0);

            Assert.IsTrue(context.AcquisitionMarket.TryQueueConstructOnOwnedPlot(plotId, optionIndex, out string queueMessage), queueMessage);
            bool progressed = context.AcquisitionMarket.ResolveWeeklyConstructionQueue(out string blockedSummary);

            Assert.IsFalse(progressed);
            Assert.AreEqual(-1, context.GetPlot(plotId).buildingId);
            StringAssert.Contains("blocked", context.AcquisitionMarket.ActiveConstructionProjects[0].statusText.ToLowerInvariant());

            context.SetSawmillStock(400);
            context.SetBlacksmithHardwareStock(50);
            for (int week = 0; week < 8 && context.GetPlot(plotId).buildingId < 0; week++)
            {
                context.AcquisitionMarket.ResolveWeeklyConstructionQueue(out _);
            }

            Assert.GreaterOrEqual(context.GetPlot(plotId).buildingId, 0, blockedSummary);
        }

        [Test]
        public void AcquisitionSaveRestoresQueuedConstructionProjects()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out _));
            Assert.IsTrue(context.AcquisitionMarket.TryQueueConstructOnOwnedPlot(plotId, optionIndex, out string queueMessage), queueMessage);

            LandLedgers.Persistence.AcquisitionSaveDto dto = context.AcquisitionMarket.CaptureSaveDto();
            Assert.AreEqual(1, dto.constructionProjects.Count);

            context.AcquisitionMarket.LoadFromSaveDto(dto);

            Assert.AreEqual(1, context.AcquisitionMarket.ActiveConstructionProjects.Count);
            StringAssert.Contains("Construction queue", context.AcquisitionMarket.BuildConstructionQueueSummary());
        }

        [Test]
        public void AcquisitionSaveRestoresExplicitConstructionQueueStates()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out _));
            Assert.IsTrue(context.AcquisitionMarket.TryQueueConstructOnOwnedPlot(plotId, optionIndex, out string queueMessage), queueMessage);

            ConstructionProjectState project = context.AcquisitionMarket.ActiveConstructionProjects[0];
            project.progressState = ConstructionProjectProgressState.WaitingForLane;
            project.blockedReason = "Builder queue is full this week.";
            project.blockedWeeks = 2;
            project.readyWeeks = 1;

            LandLedgers.Persistence.AcquisitionSaveDto dto = context.AcquisitionMarket.CaptureSaveDto();
            context.AcquisitionMarket.LoadFromSaveDto(dto);

            ConstructionProjectState restored = context.AcquisitionMarket.ActiveConstructionProjects[0];
            Assert.AreEqual(ConstructionProjectProgressState.WaitingForLane, restored.progressState);
            Assert.AreEqual("Builder queue is full this week.", restored.blockedReason);
            Assert.AreEqual(2, restored.blockedWeeks);
            Assert.AreEqual(1, restored.readyWeeks);
        }


        [Test]
        public void QueuedConstructionDetailReportsAuthorityAndSupportReview()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out _));

            Assert.IsTrue(context.AcquisitionMarket.TryQueueConstructOnOwnedPlot(plotId, optionIndex, out string queueMessage), queueMessage);

            string queueSummary = context.AcquisitionMarket.BuildConstructionQueueSummary();
            string detail = context.AcquisitionMarket.BuildConstructionQueueDetailText();

            StringAssert.Contains("builder lane", queueSummary);
            StringAssert.Contains("Pressure:", queueSummary);
            StringAssert.Contains("Next project:", detail);
            StringAssert.Contains("Stage:", detail);
            StringAssert.Contains("Start authority:", detail);
            StringAssert.Contains("Support review:", detail);
            StringAssert.Contains("Inputs needed:", detail);
            StringAssert.Contains("Next step:", detail);
            StringAssert.Contains("Current blocker:", detail);
            StringAssert.Contains("Latest weekly note:", detail);
        }

        [Test]
        public void QueuedConstructionDistinguishesBuilderLaneWaitFromInputBlocker()
        {
            using TestWorld context = TestWorld.Create();
            context.Settings.minimumLocalCarpenterCount = 1;
            Assert.IsTrue(context.TryFindBuildableOption(out int firstPlotId, out int firstOptionIndex, out _));
            Assert.IsTrue(context.TryFindAdditionalBuildableOption(firstPlotId, out int secondPlotId, out int secondOptionIndex));

            Assert.IsTrue(context.AcquisitionMarket.TryQueueConstructOnOwnedPlot(firstPlotId, firstOptionIndex, out string firstQueueMessage), firstQueueMessage);
            Assert.IsTrue(context.AcquisitionMarket.TryQueueConstructOnOwnedPlot(secondPlotId, secondOptionIndex, out string secondQueueMessage), secondQueueMessage);

            Assert.IsTrue(context.AcquisitionMarket.ResolveWeeklyConstructionQueue(out string review), review);

            ConstructionProjectState waiting = null;
            for (int i = 0; i < context.AcquisitionMarket.ActiveConstructionProjects.Count; i++)
            {
                ConstructionProjectState project = context.AcquisitionMarket.ActiveConstructionProjects[i];
                if (project != null && project.plotId == secondPlotId)
                {
                    waiting = project;
                    break;
                }
            }

            Assert.NotNull(waiting);
            Assert.AreEqual(ConstructionProjectProgressState.WaitingForLane, waiting.progressState);
            StringAssert.Contains("waiting for lane", context.AcquisitionMarket.BuildConstructionQueueSummary());
            StringAssert.Contains("waiting for builder lane", waiting.statusText);
        }

        [Test]
        public void QueuedConstructionDetailShowsBlockedStageAfterWeeklyStall()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out _));
            context.SetSawmillStock(0);
            context.SetBlacksmithHardwareStock(0);

            Assert.IsTrue(context.AcquisitionMarket.TryQueueConstructOnOwnedPlot(plotId, optionIndex, out string queueMessage), queueMessage);
            Assert.IsFalse(context.AcquisitionMarket.ResolveWeeklyConstructionQueue(out _));

            string detail = context.AcquisitionMarket.BuildConstructionQueueDetailText();

            Assert.AreEqual(ConstructionProjectProgressState.BlockedBeforeStart, context.AcquisitionMarket.ActiveConstructionProjects[0].progressState);
            StringAssert.Contains("Stage: Blocked before start", detail);
            StringAssert.Contains("Next step:", detail);
            StringAssert.Contains("Current blocker:", detail);
        }

        [Test]
        public void StartedConstructionStallsWhenBuilderLaborDisappearsThenRecovers()
        {
            using TestWorld context = TestWorld.Create();
            Assert.IsTrue(context.TryFindBuildableOption(out int plotId, out int optionIndex, out _));
            Assert.IsTrue(context.AcquisitionMarket.TryQueueConstructOnOwnedPlot(plotId, optionIndex, out string queueMessage), queueMessage);

            ConstructionProjectState project = context.AcquisitionMarket.ActiveConstructionProjects[0];
            project.weeksRequired = Mathf.Max(2, project.weeksRequired);
            Assert.IsTrue(context.AcquisitionMarket.ResolveWeeklyConstructionQueue(out string firstReview), firstReview);
            Assert.AreEqual(ConstructionProjectProgressState.InProgress, project.progressState);

            context.PopulationManager.State.people.Clear();
            Assert.IsFalse(context.AcquisitionMarket.ResolveWeeklyConstructionQueue(out _));

            Assert.AreEqual(ConstructionProjectProgressState.Stalled, project.progressState);
            StringAssert.Contains("Stalled", context.AcquisitionMarket.BuildConstructionQueueDetailText());

            context.SeedLaborPool(24);
            for (int week = 0; week < 8 && context.GetPlot(plotId).buildingId < 0; week++)
            {
                context.AcquisitionMarket.ResolveWeeklyConstructionQueue(out _);
            }

            Assert.GreaterOrEqual(context.GetPlot(plotId).buildingId, 0);
        }

    }
}
