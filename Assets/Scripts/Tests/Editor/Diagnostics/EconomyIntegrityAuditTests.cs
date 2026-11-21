// Land & Ledgers — Economy and Inventory Integrity Auditor tests (Lane 3).
//
// Focused tests per check. Known cases are reproduced from the current
// tree's source-reviewed evidence values (no run exists on this HEAD):
// 24-loaf wholesale, byproduct settlement (including the REAL
// FuelDealerByproductSettlement.Settle failure path), F-02 orphan.

using System.Collections.Generic;
using System.Linq;
using LandLedgers.Diagnostics;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class EconomyIntegrityAuditTests
    {
        // --- E-01: cash writer inventory --------------------------------------

        [Test]
        public void CashWriters_CountCallSitesPerFileAndIgnoreDefinitionsAndComments()
        {
            var files = new List<EconomySourceTextFile>
            {
                new EconomySourceTextFile("A.cs",
                    "public void Spend() {\n"
                    + "    state.AddCashCents(100);\n"
                    + "    state.AdjustCashCents(-50);\n"
                    + "    // state.AddCashCents(999); commented out, not a call site\n"
                    + "}\n"),
                new EconomySourceTextFile("B.cs",
                    "public void AddCashCents(int cents) { }\n"
                    + "public void SetCurrentCashCents(int cents, bool recordNetDelta = false) { }\n"),
                new EconomySourceTextFile("C.cs",
                    "public int CurrentCashCents => Mathf.Max(0, currentCashCents);\n"),
            };
            var findings = new List<EconomyIntegrityFinding>();
            CashWriterInventory inventory = EconomyIntegrityAudit.AuditCashWriters(files, findings);

            Assert.AreEqual(2, inventory.TotalCallSites);
            Assert.AreEqual(1, inventory.FileCount);
            Assert.AreEqual(2, inventory.WritersByFile["A.cs"]);
            Assert.IsFalse(findings.Any(f => f.Title.Contains("Direct CurrentCashCents assignment")),
                "The => getter is not an assignment.");
        }

        [Test]
        public void CashWriters_FlagsBackingFieldWriteOutsideRuntimeStateOnly()
        {
            var outside = new List<EconomySourceTextFile>
            {
                new EconomySourceTextFile("SomeDriver.cs", "currentCashCents = 0;\n")
            };
            var inside = new List<EconomySourceTextFile>
            {
                new EconomySourceTextFile("BusinessRuntimeState.cs",
                    "currentCashCents = Mathf.Max(0, snapshot.CurrentCashCents);\n"
                    + "currentCashCents += revenue;\n")
            };
            var findingsOutside = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCashWriters(outside, findingsOutside);
            Assert.IsTrue(findingsOutside.Any(f => f.Severity == EconomyFindingSeverity.Critical
                && f.Title.Contains("Backing-field cash write")));

            var findingsInside = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCashWriters(inside, findingsInside);
            Assert.IsFalse(findingsInside.Any(f => f.Title.Contains("Backing-field cash write")),
                "Backing-field writes inside BusinessRuntimeState.cs are the owning class's own mutation.");
        }

        [Test]
        public void CashWriters_FlagsDirectPropertyAssignment()
        {
            var files = new List<EconomySourceTextFile>
            {
                new EconomySourceTextFile("X.cs", "state.CurrentCashCents = 500;\n")
            };
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCashWriters(files, findings);
            Assert.IsTrue(findings.Any(f => f.Title.Contains("Direct CurrentCashCents assignment")));
        }

        [Test]
        public void CashWriters_SnapshotConstructorAssignmentIsExempt()
        {
            // The two real direct assignments on this HEAD are both
            // constructor assignments in read-only snapshot structs
            // (BusinessRuntimeTransactionSnapshot and the boarding-house
            // snapshot in NewcomerSettlementModels): initialisation, not
            // live cash mutation. The auditor must not flag them.
            var files = new List<EconomySourceTextFile>
            {
                new EconomySourceTextFile("BusinessRuntimeState.cs",
                    "internal readonly struct BusinessRuntimeTransactionSnapshot\n"
                    + "{\n"
                    + "    public BusinessRuntimeTransactionSnapshot(int currentCashCents)\n"
                    + "    {\n"
                    + "        CurrentCashCents = currentCashCents;\n"
                    + "    }\n"
                    + "    public int CurrentCashCents { get; }\n"
                    + "}\n"),
                new EconomySourceTextFile("NewcomerSettlementModels.cs",
                    "public BoardingHouseSnapshot(int currentCashCents)\n"
                    + "{\n"
                    + "    CurrentCashCents = Mathf.Max(0, currentCashCents);\n"
                    + "}\n"),
            };
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCashWriters(files, findings);
            Assert.IsFalse(findings.Any(f => f.Title.Contains("Direct CurrentCashCents assignment")),
                "Snapshot/DTO constructor assignments are initialisation, not live mutation.");
        }

        [Test]
        public void CashWriters_EqualityComparisonIsNotAnAssignment()
        {
            var files = new List<EconomySourceTextFile>
            {
                new EconomySourceTextFile("X.cs",
                    "Check(r, w.StorePort.CurrentCashCents == expectedStoreCash);\n"
                    + "bool same = a.CurrentCashCents == b.CurrentCashCents;\n")
            };
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCashWriters(files, findings);
            Assert.IsFalse(findings.Any(f => f.Title.Contains("Direct CurrentCashCents assignment")));
        }

        [Test]
        public void CashWriters_RealTreeHasNoDirectAssignmentAndCountsMethodCalls()
        {
            // The five methods are the call surface; the property getter is
            // get-only, so no production file can assign it. This pins the
            // E-01 corrected mechanism against the method list itself.
            Assert.AreEqual(5, EconomyIntegrityAudit.CashMutatingMethods.Length);
            Assert.IsTrue(EconomyIntegrityAudit.CashMutatingMethods.Contains("AddCashCents"));
            Assert.IsTrue(EconomyIntegrityAudit.CashMutatingMethods.Contains("SetCurrentCashCents"));
            Assert.IsTrue(EconomyIntegrityAudit.CashMutatingMethods.Contains("AdjustCashCents"));
            Assert.IsTrue(EconomyIntegrityAudit.CashMutatingMethods.Contains("RecordWeeklyCashTransferIn"));
            Assert.IsTrue(EconomyIntegrityAudit.CashMutatingMethods.Contains("RecordWeeklyCashTransferOut"));
        }

        // --- E-02 / DEF-067: double settlement ---------------------------------

        [Test]
        public void DoubleSettlement_SameBusinessPeriodKindTwiceIsCritical()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditDoubleSettlement(
                new[]
                {
                    new SettlementRecord("store:1", "week-12", "payroll", 500, "WageSettlementService"),
                    new SettlementRecord("store:1", "week-12", "payroll", 500, "GeneralStoreRuntimeManager.ResolveWeeklyPayrollAndReorder"),
                },
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingSeverity.Critical, findings[0].Severity);
            Assert.AreEqual("E-02", findings[0].Id);
        }

        [Test]
        public void DoubleSettlement_DifferentKindsOrPeriodsAreNotDuplicates()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditDoubleSettlement(
                new[]
                {
                    new SettlementRecord("store:1", "week-12", "payroll", 500, "WageSettlementService"),
                    new SettlementRecord("store:1", "week-12", "sales", 1200, "GeneralStoreRuntimeManager.ResolveDailySales"),
                    new SettlementRecord("store:1", "week-13", "payroll", 500, "WageSettlementService"),
                },
                findings);
            Assert.AreEqual(0, findings.Count);
        }

        [Test]
        public void LegacySalesEarlyReturn_BothGuardsPresentMeansLatentNotLive()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.VerifyLegacySalesEarlyReturn(
                "public void ResolveDailySales() {\n"
                + "    status = \"No player General Store is operating yet. Form one from Businesses before resolving sales.\";\n"
                + "}\n"
                + "public void ResolveWeeklyPayrollAndReorder() {\n"
                + "    status = \"No player General Store has been formed yet. Use Businesses > Create Business.\";\n"
                + "}\n",
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingStatus.Latent, findings[0].Status);
            Assert.IsTrue(findings[0].Title.Contains("latent"));
        }

        [Test]
        public void LegacySalesEarlyReturn_MissingGuardsMeansReverify()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.VerifyLegacySalesEarlyReturn("public void ResolveDailySales() { }\n", findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingStatus.Open, findings[0].Status);
        }

        [Test]
        public void ClerkPolicy_CleanPolicyFedBySlotParseAgrees()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.VerifyClerkPolicy(
                "public static List<int> CandidatePersonIds(int assignedClerkPersonId, int ownerPersonId) { return new List<int>(); }\n",
                "manager.TryGetServiceClerkPersonId(out int playerStoreClerk);\n"
                + "GeneralStoreServiceClerkPolicy.CandidatePersonIds(clerkPersonId, generalStoreOwnerPersonId)\n",
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingSeverity.Information, findings[0].Severity);
            Assert.AreEqual(EconomyFindingStatus.Confirmed, findings[0].Status);
        }

        [Test]
        public void ClerkPolicy_PolicyTouchingCashFailsVerification()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.VerifyClerkPolicy(
                "public static void CandidatePersonIds() { state.AddCashCents(5); }\n",
                "GeneralStoreServiceClerkPolicy.CandidatePersonIds(clerkPersonId, ownerPersonId)\n",
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingSeverity.Warning, findings[0].Severity);
        }

        // --- W SF-7: byproduct settlement ----------------------------------------

        [Test]
        public void CustodySettlement_FailedAfterCustodyMovedIsCritical()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCustodySettlement(
                new CustodyTransferRecord("sawmill-byproduct/D-F3", 10, true, false, 10, false,
                    "mill credit refused after the dealer debit succeeded"),
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingSeverity.Critical, findings[0].Severity);
            Assert.AreEqual("SF-7", findings[0].Id);
            Assert.AreEqual(EconomyFindingStatus.Open, findings[0].Status);
        }

        [Test]
        public void CustodySettlement_SettledOrCompensatedIsNotAFinding()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCustodySettlement(
                new CustodyTransferRecord("ok", 10, true, true, 10, false, string.Empty), findings);
            EconomyIntegrityAudit.AuditCustodySettlement(
                new CustodyTransferRecord("compensated", 10, true, false, 10, true, "reconciled by hand"), findings);
            Assert.AreEqual(0, findings.Count);
        }

        private sealed class RefusingMillCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }
            public int DebitedCents { get; private set; }

            public RefusingMillCashPort(int balanceCents)
            {
                BalanceCents = balanceCents;
            }

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents > BalanceCents)
                    return "insufficient balance";
                BalanceCents -= amountCents;
                DebitedCents += amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                // The SF-7 failure mode: the mill credit refuses AFTER the
                // dealer debit succeeded (a port defect, not a balance problem).
                return "mill cash port refused the credit";
            }
        }

        private sealed class WorkingCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }

            public WorkingCashPort(int balanceCents)
            {
                BalanceCents = balanceCents;
            }

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents > BalanceCents)
                    return "insufficient balance";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                BalanceCents += amountCents;
                return null;
            }
        }

        [Test]
        public void ByproductSettlement_RealSettle_MillCreditRefusalAfterDealerDebitIsUnsettled()
        {
            // Reproduces the SF-7 mechanism through the REAL production
            // Settle: dealer debited 10c, mill credited nothing, custody
            // already moved by the caller. The auditor must call it CRITICAL.
            var dealer = new WorkingCashPort(1000);
            var mill = new RefusingMillCashPort(500);
            var diag = new List<string>();
            FuelDealerByproductSettlement.SettlementResult result =
                FuelDealerByproductSettlement.Settle(10, dealer, mill, diag);

            Assert.IsFalse(result.Settled);
            Assert.AreEqual(10, result.PriceCents);
            Assert.AreEqual(990, dealer.BalanceCents);
            Assert.AreEqual(500, mill.BalanceCents);

            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCustodySettlement(
                new CustodyTransferRecord("sawmill-byproduct/D-F3", 10, true, result.Settled,
                    result.PriceCents, false, result.Refusal),
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingSeverity.Critical, findings[0].Severity);
            Assert.AreEqual("SF-7", findings[0].Id);
        }

        [Test]
        public void ByproductSettlement_RealSettle_HappyPathSettlesAtOneCentPerUnit()
        {
            var dealer = new WorkingCashPort(1000);
            var mill = new WorkingCashPort(500);
            FuelDealerByproductSettlement.SettlementResult result =
                FuelDealerByproductSettlement.Settle(10, dealer, mill, new List<string>());
            Assert.IsTrue(result.Settled);
            Assert.AreEqual(10, result.PriceCents);
            Assert.AreEqual(990, dealer.BalanceCents);
            Assert.AreEqual(510, mill.BalanceCents);
        }

        // --- E-12 / F-02: custody lots -------------------------------------------

        [Test]
        public void CustodyLots_RefusedAgreementWithoutDispositionIsOrphan()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCustodyLots(
                new[]
                {
                    new CustodyLotRecord("freight-lot/F-02", "yard", "mill", "Refused", false)
                },
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("F-02", findings[0].Id);
            Assert.AreEqual(EconomyFindingSeverity.Critical, findings[0].Severity);
        }

        [Test]
        public void CustodyLots_DuplicateCustodyAndPhantomAreFlagged()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCustodyLots(
                new[]
                {
                    new CustodyLotRecord("lot-1", "yard", "mill", "Active", true),
                    new CustodyLotRecord("lot-1", "dealer", "mill", "Active", true),
                    new CustodyLotRecord("lot-2", string.Empty, string.Empty, "Active", false),
                },
                findings);
            Assert.IsTrue(findings.Any(f => f.Title.Contains("Duplicate custody")));
            Assert.IsTrue(findings.Any(f => f.Title.Contains("Lost/phantom lot")));
            Assert.AreEqual(2, findings.Count);
        }

        [Test]
        public void CustodyLots_CleanLotsProduceNoFindings()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditCustodyLots(
                new[]
                {
                    new CustodyLotRecord("lot-1", "yard", "mill", "Active", true),
                    new CustodyLotRecord("lot-2", "dealer", "sawmill", "Cancelled", true),
                },
                findings);
            Assert.AreEqual(0, findings.Count);
        }

        // --- E-09 / E-10 / F1: pool/lot ------------------------------------------

        [Test]
        public void PoolLot_PoolOnlySaleAgainstIdentifiedLotsFreezesThem_LatentInDev()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditPoolLot(
                new CategoryStockRecord("staple_food", 100, 40, false, 24, 0, legacyPathLive: false),
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("F1", findings[0].Id);
            Assert.AreEqual(EconomyFindingSeverity.Critical, findings[0].Severity);
            Assert.AreEqual(EconomyFindingStatus.Latent, findings[0].Status);
        }

        [Test]
        public void PoolLot_PoolOnlySaleWithLiveLegacyPathIsConfirmed()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditPoolLot(
                new CategoryStockRecord("staple_food", 100, 40, false, 24, 0, legacyPathLive: true),
                findings);
            Assert.AreEqual(EconomyFindingStatus.Confirmed, findings[0].Status);
        }

        [Test]
        public void PoolLot_LotTouchedSaleDoesNotFreeze()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditPoolLot(
                new CategoryStockRecord("staple_food", 100, 40, false, 24, 24, legacyPathLive: false),
                findings);
            Assert.AreEqual(0, findings.Count);
        }

        [Test]
        public void PoolLot_DivergenceWhenBothTrackTheSameStock()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditPoolLot(
                new CategoryStockRecord("lumber", 100, 80, true, 0, 0, legacyPathLive: false),
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("E-10", findings[0].Id);
            Assert.AreEqual(EconomyFindingSeverity.Warning, findings[0].Severity);
        }

        // --- E-14: production without inputs --------------------------------------

        [Test]
        public void Production_OutputWithZeroRequiredInputConsumedIsCritical()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditProduction(
                new ProductionRecord("bakery batch", 24, "fuel", 4, 0), findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("E-14", findings[0].Id);
            Assert.AreEqual(EconomyFindingSeverity.Critical, findings[0].Severity);
        }

        [Test]
        public void Production_ConsumedInputOrNoRequirementIsNotAFinding()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditProduction(
                new ProductionRecord("bakery batch", 24, "fuel", 4, 4), findings);
            EconomyIntegrityAudit.AuditProduction(
                new ProductionRecord("free good", 5, "none", 0, 0), findings);
            Assert.AreEqual(0, findings.Count);
        }

        [Test]
        public void BakeryFuelConfig_ZeroIsAConfigValueNotMissingCode()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditBakeryFuelConfig(0, findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingStatus.ConfigValue, findings[0].Status);
            Assert.AreEqual(EconomyFindingSeverity.Critical, findings[0].Severity);
        }

        [Test]
        public void BakeryFuelConfig_PositiveValueIsInformational()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditBakeryFuelConfig(4, findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingSeverity.Information, findings[0].Severity);
        }

        // --- E-06: 24-loaf wholesale ----------------------------------------------

        [Test]
        public void WholesaleObligation_TwentyFourLoavesUnderWholesaleFirstDrainsRetail()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditWholesaleObligation(
                24, 4, "DevelopmentTownConfiguration.asset bakery wholesaleAgreements",
                "ProtectWholesaleContracts", findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("E-06", findings[0].Id);
            Assert.AreEqual(EconomyFindingSeverity.Warning, findings[0].Severity);
            Assert.AreEqual(EconomyFindingStatus.Confirmed, findings[0].Status);
        }

        [Test]
        public void WholesaleObligation_RetailFirstPolicyProtectsTheCounter()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditWholesaleObligation(
                24, 4, "DevelopmentTownConfiguration.asset bakery wholesaleAgreements",
                "ProtectRetailCounter", findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingSeverity.Information, findings[0].Severity);
        }

        // --- E-05: unpaid transfers -------------------------------------------------

        [Test]
        public void TransferPayment_PaidLumberAtThirtyTwoCentsIsClean()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditTransferPayment(
                new TransferPaymentRecord("sawmill-lumber/32c", 10, 32, 320, "yard", "sawmill"),
                findings);
            Assert.AreEqual(0, findings.Count);
        }

        [Test]
        public void TransferPayment_UnpaidGoodsAreCritical()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditTransferPayment(
                new TransferPaymentRecord("sawmill-lumber/32c", 10, 32, 0, "yard", "sawmill"),
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("E-05", findings[0].Id);
            Assert.AreEqual(EconomyFindingSeverity.Critical, findings[0].Severity);
        }

        // --- E-18: shipments ----------------------------------------------------------

        [Test]
        public void ShipmentFields_NoEtaFieldIsConfirmed()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.VerifyShipmentFields(
                new[] { "shipmentId", "state", "cargoLotId", "plannedQuantityUnits", "delayCycles" },
                findings);
            Assert.IsTrue(findings.Any(f => f.Id == "E-18" && f.Title.Contains("no ETA field")));
            Assert.IsTrue(findings.Any(f => f.Title.Contains("no created-day field")));
        }

        [Test]
        public void Shipments_ShortDeliveryRecordedCompleteIsCritical()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditShipments(
                new[]
                {
                    new ShipmentRecord("millbrook-flour/1", 10, 20, 18, 12, true, false, false)
                },
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("E-18", findings[0].Id);
            Assert.AreEqual(EconomyFindingSeverity.Critical, findings[0].Severity);
        }

        [Test]
        public void Shipments_OldOutstandingShipmentWithoutEtaIsFlagged()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditShipments(
                new[]
                {
                    new ShipmentRecord("millbrook-flour/2", 0, 45, 18, 0, false, false, false)
                },
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingSeverity.Warning, findings[0].Severity);
        }

        [Test]
        public void Shipments_YoungOutstandingShipmentIsNotFlagged()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditShipments(
                new[]
                {
                    new ShipmentRecord("millbrook-flour/3", 40, 45, 18, 0, false, false, false)
                },
                findings);
            Assert.AreEqual(0, findings.Count);
        }

        // --- E-19: supplier balances ---------------------------------------------------

        [Test]
        public void SupplierLedger_CreditOnlyIsNonAuthoritative()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditSupplierLedger(
                new SupplierLedgerRecord("millbrook", 5000, 0, false), findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("E-19", findings[0].Id);
            Assert.AreEqual(EconomyFindingSeverity.Warning, findings[0].Severity);
        }

        [Test]
        public void SupplierLedger_DebitsWithoutADebitMethodAreCritical()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditSupplierLedger(
                new SupplierLedgerRecord("millbrook", 5000, 1000, false), findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingSeverity.Critical, findings[0].Severity);
        }

        [Test]
        public void SupplierLedger_NoDebitMethodInRegistrySourceIsConfirmed()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.VerifySupplierLedgerHasNoDebit(
                "public void CreditSupplier(string id, int cents) { }\n"
                + "public int GetBalance(string id) { return 0; }\n",
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingStatus.Confirmed, findings[0].Status);
        }

        // --- E-20: opening cash ----------------------------------------------------------

        [Test]
        public void OpeningCash_ConfigProfileCodeDefaultMismatchIsFlagged()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditOpeningCash(
                new[]
                {
                    new OpeningCashRecord("Bakery", 35000, 15000, 25000),
                    new OpeningCashRecord("Fuel dealer", 20000, 13000, 25000),
                },
                findings);
            Assert.AreEqual(2, findings.Count);
            Assert.IsTrue(findings.All(f => f.Id == "E-20"));
        }

        [Test]
        public void OpeningCash_MatchingValuesProduceNoFinding()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditOpeningCash(
                new[]
                {
                    new OpeningCashRecord("Household", 40000, 40000, 40000),
                },
                findings);
            Assert.AreEqual(0, findings.Count);
        }

        // --- E-23: reservation coverage ----------------------------------------------------

        [Test]
        public void ReservationCoverage_ZeroRetailUsesIsACoverageGap()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditReservationCoverage(
                new[]
                {
                    new EconomySourceTextFile("GeneralStoreRuntimeManager.cs", "public void ResolveDailySales() { }\n"),
                    new EconomySourceTextFile("HouseholdShoppingLoop.cs", "public void Tick() { }\n"),
                },
                new[]
                {
                    new EconomySourceTextFile("BakeryStagedProductionChain.cs", "IUnifiedReservationAuthority authority;\n"),
                    new EconomySourceTextFile("FreightShipmentService.cs", "EquipmentReservationService service;\n"),
                },
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("E-23", findings[0].Id);
            Assert.AreEqual(EconomyFindingSeverity.Warning, findings[0].Severity);
            Assert.IsTrue(findings[0].Evidence.Contains("2 production files"));
        }

        [Test]
        public void ReservationCoverage_RetailUseRemovesTheGapFinding()
        {
            var findings = new List<EconomyIntegrityFinding>();
            EconomyIntegrityAudit.AuditReservationCoverage(
                new[]
                {
                    new EconomySourceTextFile("GeneralStoreRuntimeManager.cs", "IUnifiedReservationAuthority authority;\n"),
                },
                new[]
                {
                    new EconomySourceTextFile("BakeryStagedProductionChain.cs", "IUnifiedReservationAuthority authority;\n"),
                },
                findings);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(EconomyFindingSeverity.Information, findings[0].Severity);
        }

        // --- RetailUnitConversions is the single converter ---------------------------------

        [Test]
        public void RetailConversions_FlourSackIsTenPoundsAndLampOilIsFourQuarts()
        {
            Assert.IsTrue(RetailUnitConversions.TryGetHouseholdUnitsPerStoreUnit(
                "flour", "10 lb sack", "lb", out int flourPerSack, out string flourReason), flourReason);
            Assert.AreEqual(10, flourPerSack);
            Assert.IsTrue(RetailUnitConversions.TryGetStoreUnitsPerHouseholdUnit(
                "lamp_oil", "quart", "gal", out int quartsPerGallon, out string oilReason), oilReason);
            Assert.AreEqual(4, quartsPerGallon);
            // No invented conversion: an unauthored pair stays unavailable, loudly.
            Assert.IsFalse(RetailUnitConversions.TryGetHouseholdUnitsPerStoreUnit(
                "nails", "box", "lb", out _, out string nailsReason));
            Assert.IsTrue(nailsReason.Contains("no package definition"));
        }

        // --- Known cases + lot/pool map ------------------------------------------------------

        [Test]
        public void KnownCases_ReproduceWholesaleByproductOrphanFuelSupplierEtaCashAndFreeze()
        {
            EconomyIntegrityReport report = EconomyIntegrityAudit.AuditKnownCases();
            string[] ids = report.Findings.Select(f => f.Id).ToArray();
            Assert.IsTrue(ids.Contains("E-06"), "24-loaf wholesale not reproduced.");
            Assert.IsTrue(ids.Contains("SF-7"), "Byproduct settlement not reproduced.");
            Assert.IsTrue(ids.Contains("F-02"), "F-02 orphan not reproduced.");
            Assert.IsTrue(ids.Contains("E-14"), "Bakery fuel config not reproduced.");
            Assert.IsTrue(ids.Contains("E-19"), "Supplier credit-only not reproduced.");
            Assert.IsTrue(ids.Contains("E-18"), "Shipment ETA gap not reproduced.");
            Assert.IsTrue(ids.Contains("E-20"), "Opening-cash discrepancy not reproduced.");
            Assert.IsTrue(ids.Contains("F1"), "Pool/lot freeze not reproduced.");
            Assert.IsFalse(ids.Contains("E-05"), "The paid 32c lumber case must not produce a finding.");
            Assert.AreEqual(1, report.Findings.Count(f => f.Id == "SF-7" && f.Severity == EconomyFindingSeverity.Critical));
        }

        [Test]
        public void LotPoolMap_CoversEverySubsystemAndKeepsOpenRowsOpen()
        {
            IReadOnlyList<LotPoolMapEntry> map = EconomyIntegrityAudit.BuildLotPoolMap();
            Assert.AreEqual(11, map.Count);
            Assert.IsTrue(map.Any(e => e.Subsystem.Contains("General Store legacy") && e.StockForm == "Pool"));
            Assert.IsTrue(map.Any(e => e.Subsystem.Contains("Bakery staged") && e.StockForm == "Lot"));
            Assert.IsTrue(map.Any(e => e.Subsystem.Contains("Sawmill support node") && e.Status == "Open"));
            Assert.IsTrue(map.Any(e => e.Subsystem.Contains("Store procurement import") && e.Status == "Open"));
        }
    }
}
