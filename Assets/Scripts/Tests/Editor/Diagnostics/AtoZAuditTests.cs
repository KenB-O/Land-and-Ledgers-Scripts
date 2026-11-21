// Land & Ledgers — A-to-Z automated audit tests (Lane 8, §20).
//
// Focused tests per check. Fixture text stands in for the real files;
// the Editor window passes the real files from disk. Known findings are
// reproduced from the current tree's source-reviewed evidence values (no
// run exists on this HEAD): the 24-loaf wholesale removal, the SF-7
// byproduct settlement, shared-home Firewood authoring, bakery fuel-death config.

using System.Collections.Generic;
using System.Linq;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class AtoZAuditTests
    {
        private const string BakeryConfig =
            "  - key: bakery\n"
            + "    displayName: Bakery\n"
            + "    bakeryFuelUnits: 40\n"
            + "    fuelDealerIntakeUnitsPerBakeryUnit: 0\n"
            + "    wholesaleAgreements: []\n"
            + "  - key: fuel-dealer\n"
            + "    displayName: Fuel Dealer\n";

        private const string WholesaleAccountsRemoved =
            "    /// A seller whose business key has no wholesale-account entry gets\n"
            + "    /// NO demand-derived fallback — a config that lists no wholesale\n"
            + "    /// accounts at all books nothing, so New Game creates no wholesale\n"
            + "    ? $\"No wholesale accounts are configured for seller '{sellerBusinessKey}' ({sellerInstanceId}); nothing was booked.\"\n";

        private const string SawmillTransferFixed =
            "            if (dealerCash.BalanceCents < heldPriceCents)\n"
            + "            {\n"
            + "                return;\n"
            + "            }\n"
            + "            int transferred = mill.TransferByproductsToFuelDealer(dealerBusiness.InstanceId, pass9.FuelIntake, diag);\n"
            + "            FuelDealerByproductSettlement.SettlementResult settlement =\n"
            + "                FuelDealerByproductSettlement.Settle(settledUnits, dealerCash, millCash, diag);\n"
            + "                Debug.LogWarning($\"[SawmillTransfer] Day {dayIndex}: byproduct settlement failed after custody moved: {settlement.Refusal}\", this);\n";

        private const string SettlePays =
            "            result.PriceCents = FuelConversionRates.ByproductPriceCents(result.IntakeUnits);\n"
            + "            string debitProblem = dealerCash.TryDebit(result.PriceCents, \"sawmill byproduct purchase\");\n"
            + "            string creditProblem = millCash.Credit(result.PriceCents, \"sawmill byproduct sale\");\n";

        private const string HouseholdMealsFixed =
            "        /// Lane 9a (A8): the generic unspecified staple item. Never a\n"
            + "        /// servable meal item, never counted toward supply, never\n"
            + "        /// reported as a meal item — whatever policy lists it.\n"
            + "        private static bool IsUnspecifiedStaple(string itemId) =>\n"
            + "            string.Equals(itemId, HouseholdItemCatalog.UnspecifiedStapleFoodId, StringComparison.OrdinalIgnoreCase);\n"
            + "    public enum MissedMealReasonKind\n"
            + "    {\n"
            + "        NoFoodInLarder,\n"
            + "        Unknown,\n"
            + "    }\n"
            + "        public MissedMealReasonKind ReasonKind = MissedMealReasonKind.Unknown;\n";

        private static AtoZSourceEvidence FullEvidence()
        {
            return new AtoZSourceEvidence
            {
                ConfigAssetText =
                    "  - key: general-store\n"
                    + "    activityType: 3\n"
                    + BakeryConfig,
                SharedHomePrefabText = "  activityLocations:\n  - id: stove\n    type: 1\n",
                WholesaleAccountsText = WholesaleAccountsRemoved,
                ByproductSettlementText = SettlePays,
                SawmillTransferText = SawmillTransferFixed,
                HouseholdMealsText = HouseholdMealsFixed,
            };
        }

        private static AtoZFinding Find(AtoZAuditReport report, string id)
        {
            return report.Findings.First(f => f.Definition.Id == id);
        }

        // --- Catalog ----------------------------------------------------------

        [Test]
        public void Catalog_HasSourceRecordedAndRunChecks()
        {
            List<AtoZCheckDefinition> catalog = AtoZAuditRunner.BuildCatalog();
            Assert.AreEqual(7, catalog.Count(c => c.Kind == AtoZCheckKind.Source));
            Assert.AreEqual(5, catalog.Count(c => c.Kind == AtoZCheckKind.Recorded));
            Assert.AreEqual(10, catalog.Count(c => c.Kind == AtoZCheckKind.Run));
            Assert.AreEqual(22, catalog.Count);
            foreach (AtoZCheckDefinition check in catalog)
            {
                Assert.IsTrue(check.EvidenceReferences.Count > 0, check.Id + " must cite exact evidence.");
            }
        }

        // --- Wholesale (A-06: the 24-loaf obligation) -------------------------

        [Test]
        public void WholesaleConfig_ImplementedUnverifiedWhenAgreementsEmpty()
        {
            AtoZAuditReport report = AtoZAuditRunner.Evaluate(FullEvidence());
            AtoZFinding finding = Find(report, "A-06-WHOLESALE-CONFIG");
            Assert.AreEqual(AtoZFindingStatus.ImplementedUnverified, finding.Status);
            Assert.IsTrue(finding.LiveRunStillOwed);
            Assert.IsTrue(finding.Evidence.Contains("wholesaleAgreements: []"));
        }

        [Test]
        public void WholesaleConfig_UnresolvedWhenAgreementsPresent()
        {
            var evidence = FullEvidence();
            evidence.ConfigAssetText = evidence.ConfigAssetText.Replace("wholesaleAgreements: []",
                "wholesaleAgreements: [{product: bake.bread-loaf, units: 24}]");
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(evidence), "A-06-WHOLESALE-CONFIG");
            Assert.AreEqual(AtoZFindingStatus.Unresolved, finding.Status);
        }

        [Test]
        public void WholesaleFallback_ImplementedUnverifiedWhenNothingBooked()
        {
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(FullEvidence()), "A-06-WHOLESALE-FALLBACK");
            Assert.AreEqual(AtoZFindingStatus.ImplementedUnverified, finding.Status);
            Assert.IsTrue(finding.Evidence.Contains("ScenarioOpeningWholesaleAccounts.cs:147"));
        }

        [Test]
        public void WholesaleFallback_UnresolvedWhenFallbackTextMissing()
        {
            var evidence = FullEvidence();
            evidence.WholesaleAccountsText = "public static void Book() { }";
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(evidence), "A-06-WHOLESALE-FALLBACK");
            Assert.AreEqual(AtoZFindingStatus.Unresolved, finding.Status);
        }

        [Test]
        public void FurnitureProductionPersistence_RecordsCurrentSavePathButKeepsLiveCheckOpen()
        {
            AtoZCheckDefinition check = AtoZAuditRunner.BuildCatalog().First(item => item.Id == "F-S4-FURNITURE-CONTINUE");
            AtoZFinding finding = AtoZAuditRunner.RecordedFinding(check);

            Assert.AreEqual(AtoZFindingStatus.ImplementedUnverified, finding.Status);
            Assert.IsTrue(finding.LiveRunStillOwed);
            StringAssert.Contains("GenericConfiguration.ActiveProcesses", finding.Detail);
            StringAssert.Contains("fresh-scene Main Scene Continue", finding.Detail);
            StringAssert.Contains("BusinessInstanceState.CaptureSaveDto", finding.Evidence);
        }

        // --- Byproduct settlement (W SF-7) ------------------------------------

        [Test]
        public void ByproductSettlement_SuspectedFixWhenPreCheckWarningAndSettlePresent()
        {
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(FullEvidence()), "W-SF7-BYPRODUCT");
            Assert.AreEqual(AtoZFindingStatus.SuspectedFix, finding.Status);
            Assert.IsTrue(finding.LiveRunStillOwed);
            Assert.IsTrue(finding.Detail.Contains("affordability is pre-checked before custody moves"));
        }

        [Test]
        public void ByproductSettlement_UnresolvedWhenPreCheckComesAfterCustody()
        {
            var evidence = FullEvidence();
            evidence.SawmillTransferText =
                "            int transferred = mill.TransferByproductsToFuelDealer(dealerBusiness.InstanceId, pass9.FuelIntake, diag);\n"
                + "            if (dealerCash.BalanceCents < heldPriceCents) { return; }\n";
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(evidence), "W-SF7-BYPRODUCT");
            Assert.AreEqual(AtoZFindingStatus.Unresolved, finding.Status);
        }

        // --- Firewood (B) -----------------------------------------------------

        [Test]
        public void Firewood_UnresolvedWhenZeroAuthored()
        {
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(FullEvidence()), "B-FIREWOOD");
            Assert.AreEqual(AtoZFindingStatus.Unresolved, finding.Status);
            Assert.AreEqual(RefusalCauseClass.DestinationMissing, finding.Definition.CauseClass);
            Assert.IsTrue(finding.Evidence.Contains("no type: 10 entry under activityLocations"));
        }

        [Test]
        public void Firewood_ImplementedUnverifiedWhenAuthored()
        {
            var evidence = FullEvidence();
            evidence.SharedHomePrefabText += "  - id: firewood-handling\n    type: 10\n";
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(evidence), "B-FIREWOOD");
            Assert.AreEqual(AtoZFindingStatus.ImplementedUnverified, finding.Status);
            Assert.AreEqual(1, AtoZAuditRunner.CountPrefabActivityLocationType(evidence.SharedHomePrefabText, 10));
        }

        // --- Bakery fuel (E-14) ------------------------------------------------

        [Test]
        public void BakeryFuel_UnresolvedWhenConversionIsZero()
        {
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(FullEvidence()), "E-14-BAKERY-FUEL");
            Assert.AreEqual(AtoZFindingStatus.Unresolved, finding.Status);
            Assert.IsTrue(finding.Evidence.Contains("fuelDealerIntakeUnitsPerBakeryUnit: 0"));
            Assert.IsTrue(finding.Evidence.Contains("bakeryFuelUnits: 40"));
        }

        [Test]
        public void BakeryBlock_ExtractsBakeryBusinessOnly()
        {
            string block = AtoZAuditRunner.BakeryBlock(FullEvidence().ConfigAssetText);
            Assert.IsTrue(block.Contains("key: bakery"));
            Assert.IsFalse(block.Contains("key: fuel-dealer"));
            Assert.AreEqual(0, AtoZAuditRunner.ConfigIntValue(block, "fuelDealerIntakeUnitsPerBakeryUnit"));
            Assert.AreEqual(40, AtoZAuditRunner.ConfigIntValue(block, "bakeryFuelUnits"));
            Assert.AreEqual(-1, AtoZAuditRunner.ConfigIntValue(block, "noSuchKey"));
            Assert.AreEqual(string.Empty, AtoZAuditRunner.BakeryBlock("no bakery here"));
        }

        // --- Generic staple (A8) and missed-meal reasons (A5) ------------------

        [Test]
        public void GenericStaple_ImplementedUnverifiedWhenGuardPresent()
        {
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(FullEvidence()), "A-08-STAPLE-FOOD");
            Assert.AreEqual(AtoZFindingStatus.ImplementedUnverified, finding.Status);
            Assert.IsTrue(finding.Evidence.Contains("HouseholdMeals.cs:635"));
        }

        [Test]
        public void MissedMealReason_ImplementedUnverifiedWhenEnumPresent()
        {
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(FullEvidence()), "A-05-MISSED-MEAL-REASON");
            Assert.AreEqual(AtoZFindingStatus.ImplementedUnverified, finding.Status);
            Assert.IsTrue(finding.Evidence.Contains("HouseholdMeals.cs:86"));
        }

        // --- Missing evidence / recorded checks / run checks --------------------

        [Test]
        public void MissingEvidence_BlocksSourceChecksInsteadOfGuessing()
        {
            AtoZAuditReport report = AtoZAuditRunner.Evaluate(new AtoZSourceEvidence());
            Assert.AreEqual(AtoZFindingStatus.Blocked, Find(report, "A-06-WHOLESALE-CONFIG").Status);
            Assert.AreEqual(AtoZFindingStatus.Blocked, Find(report, "W-SF7-BYPRODUCT").Status);
            Assert.AreEqual(AtoZFindingStatus.Blocked, Find(report, "B-FIREWOOD").Status);
        }

        [Test]
        public void Recorded_Def046_StaysUnresolvedWithHarnessEvidence()
        {
            AtoZAuditReport report = AtoZAuditRunner.Evaluate(FullEvidence());
            AtoZFinding finding = Find(report, "DEF-046-DOUBLE-TIMELINE");
            Assert.AreEqual(AtoZFindingStatus.Unresolved, finding.Status);
            Assert.IsTrue(finding.Evidence.Contains("Expected 3, got 7"));
            Assert.IsTrue(finding.Detail.Contains("do not weaken the test"));
        }

        [Test]
        public void Recorded_StartNotWalkable_GroupsByDestinationNotSignatureAlone()
        {
            AtoZFinding finding = Find(AtoZAuditRunner.Evaluate(FullEvidence()), "W-STARTNOTWALKABLE");
            Assert.AreEqual(AtoZFindingStatus.Unresolved, finding.Status);
            Assert.AreEqual(RefusalCauseClass.OriginInvalid, finding.Definition.CauseClass);
            Assert.IsTrue(finding.Detail.Contains("2,116 sleep, 729 meal, 33 other"));
        }

        [Test]
        public void RunChecks_BlockedWithoutARun()
        {
            AtoZAuditReport report = AtoZAuditRunner.Evaluate(FullEvidence());
            foreach (AtoZFinding finding in report.Findings)
            {
                if (finding.Definition.Kind == AtoZCheckKind.Run)
                {
                    Assert.AreEqual(AtoZFindingStatus.Blocked, finding.Status, finding.Definition.Id);
                    Assert.IsTrue(finding.Detail.Contains("no run evidence was supplied"));
                }
            }
            Assert.AreEqual(10, report.CountWithStatus(AtoZFindingStatus.Blocked));
        }

        // --- Incident-core shape ------------------------------------------------

        [Test]
        public void Finding_ToIncidentReport_MapsStatusToSeverity()
        {
            Assert.AreEqual(IncidentSeverity.Information, AtoZFinding.MapStatusToSeverity(AtoZFindingStatus.VerifiedFixed));
            Assert.AreEqual(IncidentSeverity.Information, AtoZFinding.MapStatusToSeverity(AtoZFindingStatus.ImplementedUnverified));
            Assert.AreEqual(IncidentSeverity.Information, AtoZFinding.MapStatusToSeverity(AtoZFindingStatus.ExplainedLegitimate));
            Assert.AreEqual(IncidentSeverity.Warning, AtoZFinding.MapStatusToSeverity(AtoZFindingStatus.Unresolved));
            Assert.AreEqual(IncidentSeverity.Warning, AtoZFinding.MapStatusToSeverity(AtoZFindingStatus.SuspectedFix));
            Assert.AreEqual(IncidentSeverity.Unknown, AtoZFinding.MapStatusToSeverity(AtoZFindingStatus.Blocked));
            Assert.AreEqual(IncidentSeverity.Critical, AtoZFinding.MapStatusToSeverity(AtoZFindingStatus.RegressionVerified));
            Assert.AreEqual(IncidentSeverity.Critical, AtoZFinding.MapStatusToSeverity(AtoZFindingStatus.NewRegression));
        }

        [Test]
        public void Finding_ToIncidentReport_CarriesCheckIdAndEvidence()
        {
            AtoZAuditReport report = AtoZAuditRunner.Evaluate(FullEvidence());
            AtoZFinding finding = Find(report, "B-FIREWOOD");
            IncidentReport incident = finding.ToIncidentReport();
            Assert.AreEqual("B-FIREWOOD", incident.SignatureId);
            Assert.AreEqual(IncidentSeverity.Warning, incident.Severity);
            Assert.AreEqual("World", incident.OwningSystem);
            Assert.AreEqual(RefusalCauseClass.DestinationMissing, incident.CauseClass);
            Assert.IsTrue(incident.EvidenceReference.Contains("activityType: 10"));
            Assert.AreEqual(22, report.ToIncidentReports().Count);
        }

        [Test]
        public void Report_MarkdownListsEveryFindingWithStatus()
        {
            string markdown = AtoZAuditRunner.Evaluate(FullEvidence()).ToMarkdown();
            Assert.IsTrue(markdown.Contains("A-06-WHOLESALE-CONFIG"));
            Assert.IsTrue(markdown.Contains("IMPLEMENTED UNVERIFIED"));
            Assert.IsTrue(markdown.Contains("UNRESOLVED"));
            Assert.IsTrue(markdown.Contains("Run-dependent checks remain BLOCKED"));
        }

        // --- Run-check evaluation ------------------------------------------------

        [Test]
        public void EvaluateRunChecks_NoLiveRun_AllStagesBlocked()
        {
            SurvivalAcceptanceResult survival = SurvivalAcceptanceScenario.Evaluate(new SurvivalRunEvidence());
            List<AtoZFinding> findings = AtoZAuditRunner.EvaluateRunChecks(survival);
            Assert.AreEqual(10, findings.Count);
            foreach (AtoZFinding finding in findings)
            {
                Assert.AreEqual(AtoZFindingStatus.Blocked, finding.Status, finding.Definition.Id);
            }
        }

        [Test]
        public void EvaluateRunChecks_WithRunId_ReportsMissingEvidenceInsteadOfClaimingNoRun()
        {
            SurvivalAcceptanceResult survival = SurvivalAcceptanceScenario.Evaluate(
                new SurvivalRunEvidence { RunId = "run-20261121-01" });
            List<AtoZFinding> findings = AtoZAuditRunner.EvaluateRunChecks(survival);
            AtoZFinding reconciliation = findings.Find(f => f.Definition.Id == "N-ECONOMIC-RECONCILIATION");

            Assert.IsNotNull(reconciliation);
            Assert.AreEqual(AtoZFindingStatus.Blocked, reconciliation.Status);
            Assert.IsTrue(reconciliation.Detail.Contains("run-20261121-01"));
            Assert.IsTrue(reconciliation.Detail.Contains("transaction-event stream"));
            Assert.IsFalse(reconciliation.Detail.Contains("No run exists on this HEAD"));
            Assert.AreEqual("run-20261121-01", reconciliation.Evidence);

            AtoZFinding saveContinue = findings.Find(f => f.Definition.Id == "N-SAVE-CONTINUE");
            Assert.IsNotNull(saveContinue);
            Assert.AreEqual(AtoZFindingStatus.Blocked, saveContinue.Status);
            Assert.IsTrue(saveContinue.Detail.Contains("Continue midpoint"));
            Assert.IsTrue(saveContinue.Detail.Contains("run-20261121-01"));
        }
    }
}
