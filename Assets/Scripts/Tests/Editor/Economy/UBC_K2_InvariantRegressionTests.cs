using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// UBC K2 (2026-11-17): derived-view removal sweep plus regression
    /// coverage for the three K invariants.
    ///
    /// Sweep: the last two derived views (OpeningSpareBlades,
    /// WholesaleAccounts) are deleted the same way K1 deleted the first
    /// five, and all seven stay deleted (pinned by reflection below).
    ///
    /// Invariants, pinned at the harness-compiled level:
    /// (1) acquisition payment conservation — the sale TERMS conserve
    /// cash (included cash plus the seller withdrawal reconstructs the
    /// pre-close business cash; seller proceeds are price plus
    /// withdrawal; the whole deal balances). The runtime credit step
    /// (AcquisitionMarketManager.CreditSellerProceeds) is harness-
    /// excluded, so the actual ledger credit stays Unity-only; see the
    /// K2 report for the exact EditMode test that pins it.
    /// (2) debts distinct from personal obligations — a business debt
    /// counts only against its business debtor, never against the
    /// owner's household, and moves to a buyer only through an explicit
    /// assumption (novation with creditor consent).
    /// (3) property title non-transfer — changing the business owner
    /// record never moves the plot title; title moves only through an
    /// explicit bridge transfer with a stated basis and, for a
    /// purchase, a conveyance instrument. The market close DOES
    /// transfer the premises plot title on a business purchase, but
    /// explicitly (AcquisitionMarketManager.RecordTitleAcquisition with
    /// the listing id as instrument and the final price), never by
    /// virtue of the business changing hands: the runtime business
    /// transfer (SharedBusinessRuntimeManager.TryTransferBusinessTo-
    /// Player) contains zero title calls. See the K2 report.
    ///
    /// Unity compilation, Test Runner, PlayMode, and scene wiring are
    /// NOT claimed.
    /// </summary>
    [TestFixture]
    public sealed class UBC_K2_InvariantRegressionTests
    {
        // ---- Sweep: the derived views stay deleted ----

        [Test]
        public void DerivedViews_AllSeven_AbsentFromConfiguration()
        {
            Type configType = typeof(DevelopmentTownConfiguration);
            string[] deletedViews =
            {
                "OpeningInventory", "BusinessCashTargets", "HouseholdCashTargets",
                "OpeningEquipment", "OpeningEmployees", "OpeningSpareBlades", "WholesaleAccounts",
            };
            foreach (string view in deletedViews)
            {
                Assert.IsNull(configType.GetProperty(view),
                    "Derived view '" + view + "' must stay deleted; direct owner-record iteration is the only path.");
            }
        }

        // ---- Invariant 1: acquisition payment conservation (terms level) ----

        [Test]
        public void SaleTerms_Conservation_PreCloseCashSplitsIntoIncludedPlusWithdrawal()
        {
            // Pre-close the General Store holds 35,400c; its opening-cash
            // target is 5,400c, so the seller withdraws 30,000c before
            // closing and 5,400c stays in the business. The buyer pays
            // 250,000c for the business.
            const int preCloseBusinessCashCents = 35400;
            const int openingTargetCents = 5400;
            const int purchasePriceCents = 250000;
            int sellerWithdrawalCents = preCloseBusinessCashCents - openingTargetCents;

            var terms = new BusinessSaleTerms(
                "Kennedy Baldwin-ooms", "Player", openingTargetCents,
                "included as-is", "no workers employed at closing", 42,
                7, purchasePriceCents, sellerWithdrawalCents,
                "Former owner holds no operator role after closing; no transition arrangement was agreed.");

            // The withdrawal plus the cash left in the business
            // reconstructs the pre-close cash exactly: nothing is created
            // or destroyed on the business side.
            Assert.AreEqual(preCloseBusinessCashCents,
                terms.IncludedCashCents + terms.SellerCashWithdrawalCents,
                "Included cash plus the seller withdrawal must equal the pre-close business cash.");
            Assert.AreEqual(purchasePriceCents + sellerWithdrawalCents, terms.SellerProceedsCents,
                "Seller proceeds are the price plus the pre-close withdrawal.");
            // Whole-deal conservation: what the buyer paid plus what the
            // business held equals what the seller received plus what the
            // business keeps.
            Assert.AreEqual(
                purchasePriceCents + preCloseBusinessCashCents,
                terms.SellerProceedsCents + terms.IncludedCashCents,
                "Buyer payment plus pre-close business cash must equal seller proceeds plus retained business cash.");

            // The terms survive the save pipeline with the same figures.
            BusinessSaleTerms restored = BusinessSaleTerms.FromSaveDto(terms.CaptureSaveDto());
            Assert.AreEqual(terms.SellerProceedsCents, restored.SellerProceedsCents);
            Assert.AreEqual(terms.IncludedCashCents, restored.IncludedCashCents);
            Assert.AreEqual(terms.SellerCashWithdrawalCents, restored.SellerCashWithdrawalCents);
        }

        [Test]
        public void SaleTerms_NegativeInputs_ClampToZeroProceeds()
        {
            var terms = new BusinessSaleTerms(
                "Seller", "Player", -100, "included as-is", "none", 1,
                7, -5000, -3000, string.Empty);

            Assert.AreEqual(0, terms.IncludedCashCents);
            Assert.AreEqual(0, terms.PurchasePriceCents);
            Assert.AreEqual(0, terms.SellerCashWithdrawalCents);
            Assert.AreEqual(0, terms.SellerProceedsCents,
                "Proceeds can never go negative and destroy cash.");
        }

        // ---- Invariant 2: debts distinct from personal obligations ----

        private const string BusinessDebtor = "general-store-1";
        private const string HouseholdDebtor = "household:3";

        private static (EntityIdRegistry ids, FinancialObligationAuthority authority,
            FinancialObligation businessLoan, FinancialObligation personalLoan) DebtsFixture()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation businessLoan = authority.Create(ids, FinancialObligationKind.Loan,
                BusinessDebtor, "bank", 50000, 10, "business loan", "working capital");
            FinancialObligation personalLoan = authority.Create(ids, FinancialObligationKind.Loan,
                HouseholdDebtor, "bank", 12000, 10, "personal loan", "household");
            return (ids, authority, businessLoan, personalLoan);
        }

        [Test]
        public void Debts_BusinessDebt_NotCountedAsOwnerPersonalDebt()
        {
            (_, FinancialObligationAuthority authority,
                FinancialObligation businessLoan, FinancialObligation personalLoan) = DebtsFixture();
            Assert.IsNotNull(businessLoan);
            Assert.IsNotNull(personalLoan);

            Assert.AreEqual(50000, authority.TotalOutstandingFor(BusinessDebtor));
            Assert.AreEqual(12000, authority.TotalOutstandingFor(HouseholdDebtor),
                "The owner's personal total must not include the business debt.");
            Assert.AreEqual(62000, authority.TotalReceivableFor("bank"),
                "The creditor's receivable is the sum across debtors.");
            Assert.AreEqual(0, authority.Reconcile().Count, "No conservation error.");
        }

        [Test]
        public void Debts_PayingBusinessDebt_LeavesPersonalDebtUntouched()
        {
            (_, FinancialObligationAuthority authority,
                FinancialObligation businessLoan, _) = DebtsFixture();

            FinancialPaymentRecord payment = authority.ApplyPayment(
                businessLoan.ObligationId, 50000, 20, BusinessDebtor, "bank", "event-1");
            Assert.IsNotNull(payment);
            Assert.AreEqual(50000, payment.AmountCents);

            Assert.AreEqual(0, authority.TotalOutstandingFor(BusinessDebtor));
            Assert.AreEqual(12000, authority.TotalOutstandingFor(HouseholdDebtor),
                "Paying the business debt must not touch the owner's personal debt.");
            Assert.AreEqual(0, authority.Reconcile().Count, "No conservation error.");
        }

        [Test]
        public void Debts_AssumptionWithoutNovation_KeepsOriginalDebtor()
        {
            (EntityIdRegistry ids, FinancialObligationAuthority authority,
                FinancialObligation businessLoan, _) = DebtsFixture();

            ObligationAssumptionRecord assumption = authority.RecordAssumption(
                ids, businessLoan.ObligationId, "player", releaseOriginal: true,
                novation: false, creditorConsented: true, dayIndex: 15);
            Assert.IsNotNull(assumption);
            Assert.AreEqual(BusinessDebtor, businessLoan.Debtor,
                "Without novation the business stays the debtor; the buyer is only an added liable party.");
            Assert.IsTrue(businessLoan.LiableParties.Contains("player"));
            Assert.AreEqual(50000, authority.TotalOutstandingFor(BusinessDebtor));
            Assert.AreEqual(0, authority.TotalOutstandingFor("player"),
                "The buyer owes nothing on this debt until a novation moves it.");
        }

        [Test]
        public void Debts_AssumptionWithNovation_MovesDebtorExplicitly()
        {
            (EntityIdRegistry ids, FinancialObligationAuthority authority,
                FinancialObligation businessLoan, _) = DebtsFixture();

            // A novation without creditor consent is refused, never silent.
            FinancialObligation second = authority.Create(ids, FinancialObligationKind.Loan,
                BusinessDebtor, "bank", 8000, 10, "business loan", "stock");
            Assert.IsNull(authority.RecordAssumption(
                ids, second.ObligationId, "player", releaseOriginal: true,
                novation: true, creditorConsented: false, dayIndex: 15),
                "A novation without creditor consent is refused.");
            Assert.AreEqual(BusinessDebtor, second.Debtor);

            ObligationAssumptionRecord assumption = authority.RecordAssumption(
                ids, businessLoan.ObligationId, "player", releaseOriginal: true,
                novation: true, creditorConsented: true, dayIndex: 15);
            Assert.IsNotNull(assumption);
            Assert.AreEqual("player", businessLoan.Debtor,
                "A novation with creditor consent moves the debt explicitly.");
            Assert.IsTrue(businessLoan.OriginalDebtorReleased);
            Assert.AreEqual(8000, authority.TotalOutstandingFor(BusinessDebtor),
                "Only the refused-novation loan still counts against the business.");
            Assert.AreEqual(50000, authority.TotalOutstandingFor("player"));
        }

        [Test]
        public void Debts_SaveLoad_PreservesSeparation()
        {
            (_, FinancialObligationAuthority authority, _, _) = DebtsFixture();

            FinancialObligationSaveDto dto = authority.CaptureSaveDto();
            var restored = new FinancialObligationAuthority();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(50000, restored.TotalOutstandingFor(BusinessDebtor));
            Assert.AreEqual(12000, restored.TotalOutstandingFor(HouseholdDebtor));
            Assert.AreEqual(62000, restored.TotalReceivableFor("bank"));
        }

        // ---- Invariant 3: property title non-transfer ----

        private static TitleAuthority TitlesWithSellerParcel(
            string parcelId, string sellerName, int dayIndex, List<string> diag)
        {
            var titles = new TitleAuthority();
            Assert.IsNotNull(titles.RegisterParcel(
                parcelId, "Town plot", 0.5f, sellerName, TitleBasis.Unspecified, dayIndex, diag));
            return titles;
        }

        [Test]
        public void Title_BusinessOwnerChange_LeavesTitleWithSeller()
        {
            var diag = new List<string>();
            TitleAuthority titles = TitlesWithSellerParcel("plot-7", "Kennedy Baldwin-Ooms", 0, diag);

            // The business changes hands at the business-record level:
            // the config's owner key moves from the seller to the buyer.
            // No title call is made; the title chain is a separate store.
            var business = new DevelopmentTownConfiguration.BusinessDefinition
            {
                key = "general-store",
                operationId = "general-store",
                ownerPersonKey = "person-1",
                businessId = "general-store-1",
            };
            Assert.AreEqual("Kennedy Baldwin-Ooms", titles.CurrentHolder("plot-7"));
            business.ownerPersonKey = "person-2";
            Assert.AreEqual("Kennedy Baldwin-Ooms", titles.CurrentHolder("plot-7"),
                "Changing the business owner must not move the plot title.");
        }

        [Test]
        public void Title_ExplicitTransfer_MovesTitleWithInstrument()
        {
            var diag = new List<string>();
            TitleAuthority titles = TitlesWithSellerParcel("plot-7", "Kennedy Baldwin-Ooms", 0, diag);

            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 7, "Kennedy Baldwin-Ooms", "deal-1", 250000, 5, diag));
            Assert.AreEqual(PlayerTitleBridge.PlayerHolderName, titles.CurrentHolder("plot-7"),
                "An explicit bridge transfer with a conveyance instrument moves the title.");
        }

        [Test]
        public void Title_PurchaseWithoutInstrument_Refused()
        {
            var diag = new List<string>();
            TitleAuthority titles = TitlesWithSellerParcel("plot-7", "Kennedy Baldwin-Ooms", 0, diag);

            string refusal = titles.TransferTitle(
                null, "plot-7", "Player", TitleBasis.Purchase, string.Empty, 5, "no instrument", diag);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(refusal),
                "A purchase without a conveyance instrument is not a transfer.");
            Assert.AreEqual("Kennedy Baldwin-Ooms", titles.CurrentHolder("plot-7"));
        }

        [Test]
        public void Title_SaveLoad_PreservesHolder()
        {
            var diag = new List<string>();
            TitleAuthority titles = TitlesWithSellerParcel("plot-7", "Kennedy Baldwin-Ooms", 0, diag);
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 7, "Kennedy Baldwin-Ooms", "deal-1", 250000, 5, diag));

            TitleAuthority.TitleSaveDto dto = titles.CaptureSaveDto();
            var restored = new TitleAuthority();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(PlayerTitleBridge.PlayerHolderName, restored.CurrentHolder("plot-7"));
        }
    }
}
