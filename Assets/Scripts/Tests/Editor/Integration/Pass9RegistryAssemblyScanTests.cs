using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LandLedgers.Economy;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Integration
{
    /// <summary>
    /// Pass IX WS6: assembly-scan extension of the unwired-systems registry
    /// (UnwiredSystemsRegistryTests covers the hand-audited candidate list,
    /// which stays authoritative). This scan enumerates the compiled
    /// assembly for concrete, public, top-level classes in the LandLedgers
    /// namespaces and applies the SAME production-caller rule as the
    /// registry (a reference from a .cs file that is not the type's own
    /// file, not under Tests/, and not inside the type's owning directory;
    /// comments stripped).
    ///
    /// A scanned type with no production caller must be ACCOUNTED FOR by
    /// exactly one of:
    ///  - the hand-audited candidate list (mirrored below), or
    ///  - a registry entry in Docs/UnwiredSystems.md, or
    ///  - the SourceOnlyByDesign allowlist (types that are source-only on
    ///    purpose: scene/coordinator scaffolding and registered-unwired
    ///    systems), or
    ///  - the Pass IX FeatureInternalBaseline below.
    ///
    /// Why a baseline exists: this codebase organizes whole features into
    /// single files/directories (a mine's ore lots, a law office's
    /// services, an estate's records), so several hundred model/component
    /// types are referenced only inside their own feature. They are not
    /// "unwired systems" — their feature's entry types carry the wiring —
    /// but the direct-name rule cannot see that. The baseline records
    /// them ONCE, at Pass IX, so the scan can do its real job: any NEW
    /// type that appears with no production caller fails here until a
    /// human wires it, registers it, or classifies it into the baseline
    /// with the same scrutiny. The baseline may only shrink as types are
    /// wired or registered; growing it requires the classification to be
    /// visible in review, which is the point.
    ///
    /// LAND_LEDGERS_REPO may point at the tree under test; otherwise the active
    /// Unity project's Assets/Scripts checkout is discovered before the legacy fallback.
    /// </summary>
    [TestFixture]
    public sealed class Pass9RegistryAssemblyScanTests
    {
        /// <summary>Source-only-by-design types (Pass IX WS6 brief): scene/coordinator
        /// scaffolding and deliberately registered-unwired systems. Names that do not
        /// resolve to a type in this tree are inert here (kept for parity with the
        /// audit list); names that DO resolve must have no production caller.</summary>
        private static readonly string[] SourceOnlyByDesign =
        {
            "PassengerManifest", "PersonMoneyAccounts", "ScenarioCoordinator",
            "ScenarioWorldBuilder", "RealDemandSource", "BusinessValuationService",
        };

        // Mirror of UnwiredSystemsRegistryTests.CandidateTypes (hand-audited,
        // authoritative there; mirrored here so the scan's accounting is
        // self-contained). Pass XI WS1: BankRuntime added with the registry
        // entry (inert here — its directory is outside the harness
        // assembly — but kept in parity with the hand-audited list).
        private static readonly string[] CandidateTypes =
        {
            "BakeryShopRuntime", "BarberShopRuntime", "DoctorPracticeRuntime",
            "GrainElevatorShopRuntime", "GrainMillShopRuntime", "HotelShopRuntime",
            "LumberYardShopRuntime", "NewspaperRuntime", "RestaurantShopRuntime",
            "SaloonRuntime", "SawmillShopRuntime", "TailorShopRuntime",
            "BankRuntime",
            "StagedProductionProcess", "ProductionDueEventDriver",
            "BakeryStagedProductionChain", "ForgeRepairChain",
            "StoreRestockService",
            "RealDemandSource",
        };

        // Pass IX baseline of feature-internal types (see fixture summary).
        // GENERATED from the scan's first run against this tree and then
        // reviewed cluster by cluster (mine, lawyer, estates, labor
        // managers, generic retail, read models, save DTOs, scene-wired
        // components). Sorted for reviewable diffs.
        private static readonly string[] FeatureInternalBaseline =
        {
            "AbstractAdverseClaimNote", "ActiveProcessRecord", "ActivityTransition", "AptitudeStore",
            // Pass IX post-merge classification (coordinator, wave-2 integration): the WS5
            // appraisal service's result and ledger-record DTOs live in AssetSaleAppraisal.cs
            // and are consumed inside that file by AssetSaleAppraisalService /
            // AssetSaleExecutionService, whose production callers sit in SimulationDrivers
            // (the resolver seam). File-internal DTOs of a wired service: feature-internal.
            "AssetAppraisalResult", "AssetSaleRecord",
            "AutonomousSocietyDriver", "AutonomousSocietySaveDto", "AutonomousWorkGenerator", "AvenueRecommendation",
            "BakeryChainContext", "BakeryFlourDispenseLine", "BakeryFlourLot", "BakeryFlourStock",
            "BakeryFuelDispenseLine", "BakeryFuelLot", "BakeryFuelStock", "BakeryPriceSchedule",
            "BakeryStagedBreadLot", "BakeryStagedWasteRecord", "BankCreditParticipant", "BankLoanUnderwritingResult",
            "BorrowerLenderMemory", "BuildingFootprintLinker", "BuildingFootprintView",
            // Pass IX review fix R-13: "BusinessCashPostagePayer" removed from this
            // baseline — since R-03 it is constructed by RecruitmentService
            // (Domains/Economy/Recruitment), a genuine production caller outside
            // its owning PostOffice directory, so the scan accounts it by caller.
            // "HouseholdLedgerPostagePayer" STAYS: its only production
            // construction is RegisteredMailService in the SAME PostOffice
            // directory, which the direct-name rule counts as feature-internal.
            "BusinessCashStore", "BusinessDefinition", "BusinessEconomyDefinition", "BusinessInputNeed",
            "BusinessInstanceSellerAdapter", "BusinessLoanRecord", "BusinessMainFocusDefinition", "BusinessMainFocusState",
            "BusinessNamedPayableRecord", "BusinessOperationsDefinition", "BusinessReputationChangeResult", "BusinessSaleTerms",
            "BusinessSaveRecord", "BusinessStockRecord", "BusinessWorkReadModel", "BusinessWorkReadModelBuilder",
            // Pass XI coordinator integration: ReadModels/Reporting entered the canonical
            // harness subset so the WS3 owner-reporting tests run there (sources.txt
            // gained BusinessReportBuilder.cs, PortfolioReportBuilder.cs, ReportDtos.cs,
            // ReportSnapshots.cs, ReportPeriod.cs, LedgerEntry.cs). The 20 report
            // DTO/snapshot types below have no production caller outside their feature
            // (they are consumed by the builders and by Unity presentation) and are
            // classified feature-internal, the same precedent as BusinessWorkReadModel
            // and the save-DTO clusters. This growth of the baseline is the visible
            // review the fixture summary requires; the builders themselves are
            // NOT listed here because they are static classes (abstract plus
            // sealed in IL) and the scan filter excludes abstract types, so
            // they escape the scan on that ground — corrected in Pass XI
            // WS6 first-gate (R2): the earlier claim that they "are
            // accounted for by production callers" was wrong, as they have
            // zero production .cs callers (only comments and tests). The
            // scan test logic itself is unchanged; this is a comment
            // correction only, and the builders remain presentation-fed
            // read models, not hidden unwired types.
            "BusinessCapacitySummary", "BusinessCategorySnapshot", "BusinessCategorySummary",
            "BusinessFinancialRollup", "BusinessInventorySummary", "BusinessMonthlySummary",
            "BusinessOperationsSummary", "BusinessReportGap", "BusinessReportHeader",
            "BusinessReportSnapshot", "BusinessStaffingSummary", "BusinessWeeklySummary",
            "BusinessWorkerSnapshot", "BusinessWorkerSummary",
            "PortfolioBalanceDriftSummary", "PortfolioBusinessSummaryLine",
            "PortfolioFinancialRollup", "PortfolioMonthlySummary",
            "PortfolioReportSnapshot", "PortfolioWeeklySummary",
            // Pass XII Worker C classification: the yearly/all-time rollup
            // DTOs and the workload-staffing read-model types below are
            // the same cluster as the Pass XI report DTOs above — data
            // carriers of wired features (report builders in the same
            // Reporting directory; the payroll authority in
            // BusinessRuntimeState), consumed by builders and Unity
            // presentation, not standalone unwired systems. They are
            // feature-internal, not UnwiredSystems candidates: the
            // yearly/all-time builders are fed by monthly summaries, the
            // staffing model is fed by task/schedule/payroll inputs, and
            // ManualWagePaymentRecord is the persistence record of the
            // exactly-once wage guard inside BusinessRuntimeState.
            "BusinessYearlySummary", "BusinessAllTimeSummary",
            "WorkloadStaffingReadModel", "WorkloadTaskInput", "WorkloadWorkerInput",
            "WorkloadLaborDemandInput", "WorkloadWageInput", "WorkloadWorkerRow",
            "ManualWagePaymentRecord",
            "CapturedScenarioSelection", "CarriedGoodsBatch", "CausalDemographicEvent", "CausalEmploymentRecord",
            "CausalObligationSummary", "ChainLinkSummary", "CivicTreasuryLedgerEntry", "ConstructionAcceptanceStage",
            // Pass XI WS2 classification: the Construction feature cluster below
            // enters the harness assembly with the construction driver. Its entry
            // types (NpcConstructionDriver, NpcConstructionCommissioner,
            // ConstructionProjectExecutor) are wired through SimulationDrivers;
            // these component/record types are consumed inside the feature, so
            // they are feature-internal, the same precedent as the mine and
            // lawyer clusters and the WS3 report DTOs.
            "ConstructionBid", "ConstructionBidEstimateLine", "ConstructionBidRequest", "ConstructionBillingEvent",
            "ConstructionChangeOrder", "ConstructionContract", "ConstructionContractBook", "ConstructionContractCapacityReading", "ConstructionContractParty", "ConstructionDefectRecord", "ConstructionFreightPlan",
            "ConstructionMaterialFreightCoordinator", "ConstructionMaterialSale", "ConstructionMaterialSaleLine",
            "ConstructionPaymentMilestone", "ConstructionPaymentObligation", "ConstructionPaymentTerms",
            "ConstructionProcurementService", "ConstructionProgressBillingPolicy", "ConstructionProgressBillingService",
            "ConstructionSiteStockpile", "ConstructionSpecSection", "ConsumedMaterialLine",
            "ContactLink", "ContestSettlementTerm", "ContractObligation", "ConversationChannel",
            "ConversationOpportunityService", "CreditCounterparty", "CreditEvent", "CreditEvidenceRecord",
            "CreditFacilityRecord", "CreditOfferNegotiationRecord", "CropCalibration", "CustomerSpecialOrderState",
            "CustomerVisit", "DefaultTaskDurationEstimator", "DelegateDecision", "DemographicDriverContext",
            "DiscoveredOpportunity", "DisputeRepresentationService", "DomainFamiliarityStore", "DomesticAssignment",
            "DraftTeamReservation", "DraftedDeed", "DraftedWillEntry", "DurationEstimatorV2Adapter",
            "EmploymentFingerprintRecord", "EmploymentOffer", "EntityReferenceDiagnostic", "EquipmentAccessGrant",
            "EquipmentAvailability", "EquipmentEvent", "EquipmentHold", "EquipmentInspectionRecord", "EquipmentMisgradeDispute",
            "EquipmentMisgradeDisputeRegister", "EquipmentReadModel", "EquipmentReadModelBuilder", "EquipmentSalvageLedger",
            "EquipmentScrapSale", "EstateAbatementRecord", "EstateAccount", "EstateAccountEntry",
            "EstateAdministratorAppointment", "EstateCreditorClaim", "EstateExecutorAct", "EstateInventoryLine",
            "EstateLifecycleProgress", "EstateMinorCustody", "EstateUndividedBusinessShare", "ExecutorCompensationSchedule",
            "ExperienceFormationInputs", "ExperienceFormationService", "ExperienceLedger", "ExternalCounterparty",
            "ExternalCounterpartySaveDto", "ExternalCounterpartySupplyDto", "ExternalMarketAuthority", "ExternalMarketAuthoritySaveDto",
            "ExternalTradeRecord", "ExternalTradeRecordSaveDto", "FallbackDurationEstimator", "FamilyUnitPlan",
            "FarmPlowService", "FeeLineItem", "FinancingEstimate", "FinancingEvaluator",
            "ForeclosureBid", "ForeclosureCase", "ForgeChainContext", "ForgeRepairOrder",
            "FreightRoute", "GenericBusinessSetupTemplate", "GenericEquipmentInstallationState", "GenericInventoryLot",
            "GenericProductCatalog", "GenericRetailConfiguration", "GoodsCounterRecord", "GuarantyRecord",
            "HeirShare", "HistoricalPersonRecord", "HoldingRecord", "HorseHousingState",
            "HorseRentalRecord", "HorseRentalRequest", "HorseRentalService", "HorseRentalServiceSaveDto",
            "HorseSale", "HouseholdErrandPlanClaimEntry", "HouseholdErrandPlanIntEntry", "HouseholdErrandPlanRegistry",
            "HouseholdLedgerPostagePayer", "HouseholdMembershipProjectionResult", "HouseholdMoveResult", "HouseholdMoveService",
            "HouseholdOpportunityLog", "HouseholdOpportunityLogSaveDto", "HouseholdPressureInput", "HouseholdShelterSuitability",
            "HouseholdShelterSuitabilityEvaluator", "HouseholdShoppingReadModel", "HouseholdSpendingPolicy", "HousingDecisionEvent",
            "HousingDecisionLog", "HousingOpportunityAdapter", "HousingRejectedAlternative", "HousingSearchDelegatedStep",
            "ImportOrder", "ImportService", "InformationPacket", "JobDecision",
            "JobDecisionInput", "JobSecurityDecision", "JourneyEdge", "JourneyEdgeAttributes",
            "KnownSupplierRecord", "LaborDemandRow", "LawyerContractService", "LawyerDeedService",
            "LawyerDraftedContract", "LawyerFeeSchedule", "LawyerOfficeRequirements", "LawyerTitleSearchService",
            "LawyerWillDraftingService", "LegalFeeInvoice", "LenderCommitment", "LivelihoodReadModel",
            "LivelihoodReadModelBuilder", "LoanOffer", "LoanPaymentDue", "LoanPaymentSchedule",
            "ManagerActivityLog", "ManagerActivityRecord", "ManagerActivitySaveDto", "ManagerBorrowExecutor",
            "ManagerDecisionExecutionLedger", "ManagerExecutedActionRecord", "ManagerExecutionContext", "ManagerOpenRecruitmentExecutor",
            "ManagerOrderMaintenanceExecutor", "ManagerPendingRequest", "ManagerPriceChangeExecutor", "ManagerProcureExecutor",
            "ManagerProcurementShipment", "ManagerScheduleExecutor", "ManagerSellAssetExecutor", "ManagerWorkAllocator",
            "MealInventorySnapshot", "MigrationEvaluation", "MigrationEvaluationInput", "MigrationOutcome",
            "MineAssayOffice", "MineAssayOfficeSaveDto", "MineAssayReport", "MineAssayReportSaveDto",
            "MineAssayResult", "MineAssayResultSaveDto", "MineAssaySample", "MineAssaySampleSaveDto",
            "MineAssayService", "MineCostEntry", "MineCostEntrySaveDto", "MineCostLedger",
            "MineCrewAssignment", "MineCrewAssignmentSaveDto", "MineHoist", "MineHoistRegister",
            "MineHoistSaveDto", "MineLevel", "MineLevelSaveDto", "MineOreClassCutoffs",
            "MineOreDispenseLine", "MineOreLot", "MineOreLotSaveDto", "MineOreShipmentOrder",
            "MineOreShipmentOrderSaveDto", "MineOreShipmentService", "MineOreStock", "MinePreservationBurden",
            "MineSettlementBreakdown", "MineShaft", "MineShaftPlan", "MineShaftSaveDto",
            "MineShipmentGradeSummary", "MineShipmentGradeSummarySaveDto", "MineShipmentParcel", "MineSmelterLink",
            "MineSmelterSettlementTerms", "MineSmelterSettlementTermsSaveDto", "MineTimberingLedger", "MineTimberingRecord",
            "MineTimberingRecordSaveDto", "MineVein", "MineVeinSaveDto", "MineWorkFrontAllocation",
            "MissedSaleRecord", "MotivePowerSource", "NailProductionRun", "NegotiationAptitudeEvaluator",
            "NegotiationAptitudeState", "NpcAcquisitionPlan", "NpcAcquisitionResult", "NpcBuildOverrunEvent", "NpcBusinessAcquisitionResult",
            "NpcBusinessBorrowContext", "NpcBusinessCloseResult", "NpcBusinessDemandPlanner", "NpcBusinessDemandResult",
            "NpcBusinessEvent", "NpcBusinessInputNeed", "NpcBusinessPurchase", "NpcFinancingPlan",
            "NpcFormationAgreement", "NpcInvestigationFinding", "NpcLenderQuote", "NpcLifecycleContext",
            "NpcLiquidationSale", "NpcPriceNegotiationResult", "NpcPropertyAcquisitionService", "NpcPropertyDecision",
            "NpcPropertyFinancingAvailability", "NpcPropertyHolding", "NpcPropertyMaintenanceNeed", "NpcPropertySaleResult",
            "NpcPropertyTenancy", "NpcSellerResponse", "ObligationLinkRecord", "OccupationCalibrationReport",
            "OffScreenBatchStep", "OffScreenHouseholdRecord", "OffScreenReconciler", "OfferEmploymentPair",
            "ParcelRecord", "PathingResult", "PedigreeClaim", "PersonActivityReadModel",
            "PersonActivityReadModelBuilder", "PersonActivitySnapshot", "PersonBirthDateRecord", "PersonCapabilityStore",
            "PersonMinutesRecord", "PersonSkillState", "PlannedPerson", "PlannedShoppingTrip",
            "PlannedShoppingTripSaveDto", "PlowWorkHooks", "PlowWorkOrder", "PositionState",
            "PostalContract", "PostalHandoff", "PostalMoneyOrder", "PostalMoneyOrderAdvice",
            "PostalMoneyOrderClaim", "PostalMoneyOrderOffice", "PostalSettlementPosition", "PrefabHandoffRecord",
            "PrivateLenderHouseholdCreditFacility", "ProductPackageDefinition", "ProductionStageState", "ProductionTaskHandoffDriver",
            "ProfessionDefinition", "RecruitmentLetter", "RegisteredCustodyEntry", "RegisteredMailRecord",
            "RegistryNumberState", "RejectedShoppingAlternative", "RentalFleet", "RepresentationEngagement",
            "ReputationChangeResult", "ReservationRow", "ResidentialConditionRecord", "ResidentialConditionRegistry",
            "RetailDeliveryAcceptance", "RetailRestockJob", "RetailServiceDesk", "RetailServiceDeskSaveDto",
            "RetailServiceHooks", "RoleDefinition", "RuntimeEntityDirectory", "ScenarioBootstrapComposition",
            "ScenarioBootstrapPlan", "ScenarioEconomyPolicy", "ScenarioFixedBusiness", "ScenarioHeadlineSnapshot",
            // Pass XI coordinator integration: the three Saloon/ScenarioModels files
            // entered the canonical harness subset so the WS6 first-gate seam tests
            // run there. The 5 types below have genuine production callers in
            // harness-excluded scenario files, so the scan cannot see them:
            // ScenarioGoal and ScenarioObjective are used by ScenarioService,
            // ScenarioAsset, and both scenario definitions; PopulationInclusionRules
            // and PlayerHouseholdDeclaration by FirstLedgerScenario and
            // ScenarioAsset (and the director for the declaration);
            // SaloonCircuitGoalEvaluator by SaloonCircuitBootstrap. They are
            // classified feature-internal on that production-caller evidence,
            // the same subset-visibility precedent as the Reporting DTOs.
            // This growth of the baseline is the visible review the fixture
            // summary requires; no genuinely unwired type is hidden by it.
            "ScenarioGoal", "ScenarioObjective", "PopulationInclusionRules", "PlayerHouseholdDeclaration", "SaloonCircuitGoalEvaluator",
            "ScenarioInventoryGrant", "ScenarioPlayerStart", "ScenarioPreset", "ScenarioPreviewReport",
            "ScenarioPropertyGrant", "ScenarioSeedPropertyGrant", "SecurityInterestRecord", "SeizedCollateralRecord",
            "SellerFinanceDecision", "SellerFinanceDecisionInput", "SellerFinanceDecisionService", "SellerFinanceNegotiationEvent",
            "SellerFinancePolicy", "SettledWeekMarker", "ShelfUnitsEntry", "ShoppingDiagnosticEvent",
            "ShoppingTripDriver", "SimpleDueEventScheduler", "SimpleDueEventSchedulerSaveDto", "SkillTaskDurationEstimator",
            "SmithingRecipe", "SocietyReconciliation", "StageLaborBaseline", "StagedBakeryBatch",
            "StagedBakeryBatchSaveDto", "StandingInstruction", "StandingInstructionRegister", "StandingInstructionSaveDto",
            "StartingWorldConfiguration", "StockProvenanceRecord", "StoreCategoryOffer", "StoreTradingHours",
            "SubdivisionRequest", "SubdivisionResult", "SubdivisionValidation", "SupplierOrderLine",
            "SupplierPriceTier", "SupplierTrustEvaluator", "SupportContext", "SupportRequirement",
            "SupportabilityReport", "TaskRow", "TaxSale", "TaxSaleBid",
            "TenancyNegotiationRequest", "TitleAbstract", "ToolKitDefinition", "TownGrid",
            "TradeCreditInvoiceTerms", "UsedEquipmentListing", "UsedEquipmentMarketBook", "UsedEquipmentMarketRules",
            "UsedEquipmentPriceGuidance", "UsedEquipmentSale", "ValuationTuning", "WillContest",
            "WillContestCostSchedule", "WillContestGroundClaim", "WorkAssignment", "WorkEvent",
            "WorkerRow", "WorkforceStaffingReadModel", "WorkstationComponentRequirement", "WorldInspectableTarget",
        };

        private static string ResolveRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (HasRegistryDocument(fromEnv))
                return Path.GetFullPath(fromEnv);

            // In the Unity project the nested Scripts checkout is Assets/Scripts.
            // Resolve it from the active project instead of assuming an account-specific path.
            string projectScripts = Path.GetFullPath(Path.Combine(Application.dataPath, "Scripts"));
            if (HasRegistryDocument(projectScripts))
                return projectScripts;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "workspace", "land-and-ledgers", "repo");
        }

        private static bool HasRegistryDocument(string repoRoot)
        {
            return !string.IsNullOrWhiteSpace(repoRoot)
                && System.IO.Directory.Exists(repoRoot)
                && File.Exists(Path.Combine(repoRoot, "Docs", "UnwiredSystems.md"));
        }

        private sealed class SourceIndex
        {
            public readonly List<KeyValuePair<string, string>> Files = new List<KeyValuePair<string, string>>();

            public SourceIndex(string repoRoot)
            {
                foreach (string file in System.IO.Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(repoRoot.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    if (rel.StartsWith(".git/", StringComparison.Ordinal) || rel.Contains("/worktrees/"))
                        continue;
                    string text;
                    try { text = StripComments(System.IO.File.ReadAllText(file)); }
                    catch (IOException) { continue; }
                    Files.Add(new KeyValuePair<string, string>(rel, text));
                }
            }

            private static string StripComments(string text)
            {
                text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                string[] lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    int idx = lines[i].IndexOf("//", StringComparison.Ordinal);
                    if (idx >= 0)
                        lines[i] = lines[i].Substring(0, idx);
                }
                return string.Join("\n", lines);
            }
        }

        private static string FindDeclaringFile(SourceIndex index, string typeName)
        {
            var pattern = new Regex(@"\bclass\s+" + Regex.Escape(typeName) + @"\b");
            foreach (var file in index.Files)
            {
                if (file.Key.StartsWith("Tests/", StringComparison.Ordinal))
                    continue;
                if (pattern.IsMatch(file.Value))
                    return file.Key;
            }
            return null;
        }

        private static bool HasProductionCaller(SourceIndex index, string typeName, string ownFile, string ownDir)
        {
            var pattern = new Regex(@"\b" + Regex.Escape(typeName) + @"\b");
            foreach (var file in index.Files)
            {
                string rel = file.Key;
                if (rel.Equals(ownFile, StringComparison.Ordinal))
                    continue;
                if (rel.StartsWith("Tests/", StringComparison.Ordinal))
                    continue;
                if (!string.IsNullOrEmpty(ownDir) && rel.StartsWith(ownDir + "/", StringComparison.Ordinal))
                    continue;
                if (pattern.IsMatch(file.Value))
                    return true;
            }
            return false;
        }

        private static HashSet<string> ParseRegisteredTypeNames(string repoRoot)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            string docPath = System.IO.Path.Combine(repoRoot, "Docs", "UnwiredSystems.md");
            Assert.IsTrue(System.IO.File.Exists(docPath),
                "UnwiredSystems registry document not found at " + docPath +
                ". Set LAND_LEDGERS_REPO to the repository checkout to check.");
            bool inTable = false;
            foreach (string rawLine in System.IO.File.ReadAllLines(docPath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("| Type |", StringComparison.Ordinal)) { inTable = true; continue; }
                if (!inTable) continue;
                if (!line.StartsWith("|", StringComparison.Ordinal)) break;
                if (line.StartsWith("|---", StringComparison.Ordinal)) continue;
                string[] cells = line.Split('|').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
                if (cells.Length >= 1)
                    names.Add(cells[0]);
            }
            return names;
        }

        [Test]
        public void AssemblyScan_EveryScannedType_IsAccountedFor()
        {
            string repoRoot = ResolveRepoRoot();
            var index = new SourceIndex(repoRoot);
            HashSet<string> registered = ParseRegisteredTypeNames(repoRoot);
            var accounted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in CandidateTypes) accounted.Add(name);
            foreach (string name in registered) accounted.Add(name);
            foreach (string name in SourceOnlyByDesign) accounted.Add(name);
            foreach (string name in FeatureInternalBaseline) accounted.Add(name);

            Assembly assembly = typeof(SharedCategoryStockLedger).Assembly;
            List<Type> scanned = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && !t.IsNested
                    && t.Namespace != null
                    && (t.Namespace == "LandLedgers" || t.Namespace.StartsWith("LandLedgers.", StringComparison.Ordinal))
                    && !t.Namespace.Contains("EditorTests")
                    && !t.Name.Contains("`") && !t.Name.Contains("<"))
                .ToList();
            Assert.IsNotEmpty(scanned, "The assembly scan enumerated nothing — the scan itself is broken.");

            var unaccounted = new List<string>();
            int withCaller = 0, skippedNoSourceFile = 0;
            foreach (Type type in scanned)
            {
                string declaring = FindDeclaringFile(index, type.Name);
                if (declaring == null)
                {
                    // Not declared in this source tree (e.g. a harness shim
                    // type): the scan governs repository types only.
                    skippedNoSourceFile++;
                    continue;
                }
                string ownDir = declaring.Contains("/")
                    ? declaring.Substring(0, declaring.LastIndexOf('/'))
                    : string.Empty;
                if (HasProductionCaller(index, type.Name, declaring, ownDir))
                {
                    withCaller++;
                    continue;
                }
                if (accounted.Contains(type.Name))
                    continue;
                unaccounted.Add(type.Name + " (" + declaring + ")");
            }

            Assert.IsEmpty(unaccounted,
                "Concrete LandLedgers types with NO production caller and no accounting. " +
                "Wire the type, register it in Docs/UnwiredSystems.md, or classify it " +
                "(candidate / source-only-by-design / feature-internal baseline) with review:\n" +
                string.Join("\n", unaccounted));
        }

        /// <summary>
        /// A3-09 made machine-checkable: RealDemandSource is registered in
        /// Docs/UnwiredSystems.md AND still has no production caller — the
        /// registration is honest, and the day someone wires it, this test
        /// (and RegisteredTypes_HaveNoProductionCallers) forces the
        /// deliberate registry update.
        ///
        /// Note on the allowlist: the audit list also names
        /// ScenarioWorldBuilder as source-only, but in this tree it IS
        /// referenced from Orchestration/Scenarios/ScenarioUnityWiring.cs
        /// (production wiring), so the scan's accounting never needs its
        /// allowlist entry — the entry is inert, not load-bearing, and is
        /// kept for parity with the audit list. PassengerManifest,
        /// PersonMoneyAccounts, and BusinessValuationService do not exist
        /// in this tree at all (verified by source search at Pass IX);
        /// their entries are likewise inert.
        /// </summary>
        [Test]
        public void AssemblyScan_RealDemandSource_IsRegisteredAndStillUnwired()
        {
            string repoRoot = ResolveRepoRoot();
            var index = new SourceIndex(repoRoot);
            HashSet<string> registered = ParseRegisteredTypeNames(repoRoot);
            Assert.IsTrue(registered.Contains("RealDemandSource"),
                "RealDemandSource must stay registered in Docs/UnwiredSystems.md while it is unwired (A3-09).");
            string declaring = FindDeclaringFile(index, "RealDemandSource");
            Assert.IsNotNull(declaring);
            string ownDir = declaring.Contains("/")
                ? declaring.Substring(0, declaring.LastIndexOf('/'))
                : string.Empty;
            Assert.IsFalse(HasProductionCaller(index, "RealDemandSource", declaring, ownDir),
                "RealDemandSource gained a production caller — complete the wiring deliberately " +
                "(a real PurchaseIntent executor) and remove its registry entry, per A3-09.");
        }
    }
}
