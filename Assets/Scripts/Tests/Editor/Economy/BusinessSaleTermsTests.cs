using LandLedgers.Economy;
using LandLedgers.Persistence;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// P1: explicit business sale terms (Canon XXVII Part IX §9.1). Sale of a
    /// business must NOT silently transfer all business cash — the agreement
    /// states what cash, inventory and employee offers are included, and the
    /// terms persist with the business through the save pipeline.
    /// </summary>
    [TestFixture]
    public sealed class BusinessSaleTermsTests
    {
        private static BusinessSaleTerms SampleTerms()
        {
            return new BusinessSaleTerms(
                "Kennedy Baldwin-ooms",
                "Local Buyer - Blacksmith",
                12500,
                "included as-is: 40 units of hardware",
                "buyer offers Ada Smith continued employment as Smith at 1200c/week",
                42);
        }

        [Test]
        public void SaleTerms_RoundTrip_PreservesAllFields()
        {
            BusinessSaleTerms terms = SampleTerms();

            BusinessSaleTermsSaveDto dto = terms.CaptureSaveDto();
            BusinessSaleTerms restored = BusinessSaleTerms.FromSaveDto(dto);

            Assert.AreEqual("Kennedy Baldwin-ooms", restored.SellerDisplayName);
            Assert.AreEqual("Local Buyer - Blacksmith", restored.BuyerDisplayName);
            Assert.AreEqual(12500, restored.IncludedCashCents);
            Assert.AreEqual("included as-is: 40 units of hardware", restored.InventoryStatement);
            Assert.AreEqual(
                "buyer offers Ada Smith continued employment as Smith at 1200c/week",
                restored.EmployeeOfferStatement);
            Assert.AreEqual(42, restored.ClosingDayIndex);
        }

        [Test]
        public void SaleTerms_BuildSummary_StatesCashInventoryAndEmployees()
        {
            string summary = SampleTerms().BuildSummary();

            StringAssert.Contains("12500c", summary, "Cash inclusion must be explicit.");
            StringAssert.Contains("40 units of hardware", summary, "Inventory inclusion must be explicit.");
            StringAssert.Contains("Ada Smith", summary, "Employee offers must be explicit.");
        }

        [Test]
        public void SaleTerms_DefaultTerms_AreEmptyAndSafe()
        {
            var terms = new BusinessSaleTerms();

            Assert.AreEqual(0, terms.IncludedCashCents);
            Assert.AreEqual(string.Empty, terms.SellerDisplayName);
            Assert.AreEqual(string.Empty, terms.InventoryStatement);
            Assert.DoesNotThrow(() => terms.CaptureSaveDto());
            Assert.DoesNotThrow(() => terms.BuildSummary());
        }

        [Test]
        public void BusinessInstance_RecordsAndPersistsSaleTerms()
        {
            BusinessProfileDefinition profile =
                BusinessProfileDefinition.CreateFallback(BusinessType.Blacksmith, "Test Smithy");
            BusinessInstanceState business =
                BusinessInstanceState.Create("test_sale_terms", profile, 0, BusinessOwnerIdentity.Player());

            business.RecordSaleTerms(SampleTerms());

            BusinessInstanceSaveDto dto = business.CaptureSaveDto();
            BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(dto);

            Assert.IsNotNull(restored);
            Assert.AreEqual(12500, restored.SaleTerms.IncludedCashCents);
            Assert.AreEqual("Local Buyer - Blacksmith", restored.SaleTerms.BuyerDisplayName);
            Assert.AreEqual(
                "buyer offers Ada Smith continued employment as Smith at 1200c/week",
                restored.SaleTerms.EmployeeOfferStatement);
        }

        [Test]
        public void BusinessInstance_UnsoldBusiness_HasEmptySaleTerms()
        {
            BusinessProfileDefinition profile =
                BusinessProfileDefinition.CreateFallback(BusinessType.Saloon, "Test Saloon");
            BusinessInstanceState business =
                BusinessInstanceState.Create("test_sale_terms_unsold", profile, 0, BusinessOwnerIdentity.Player());

            Assert.AreEqual(0, business.SaleTerms.IncludedCashCents);
            BusinessInstanceSaveDto dto = business.CaptureSaveDto();
            BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(dto);
            Assert.AreEqual(0, restored.SaleTerms.IncludedCashCents);
        }

        [Test]
        public void SaleReviewFlow_RequiresListingTermsAndSeparateConfirmation()
        {
            BusinessSaleReviewFlow flow = new();

            Assert.IsFalse(flow.CanConfirm(12), "A single click must not close the sale.");
            Assert.AreEqual(BusinessSaleReviewStage.ListingReviewed, flow.ReviewListing(12));
            Assert.IsFalse(flow.CanConfirm(12), "Listing review is not final confirmation.");
            Assert.AreEqual(BusinessSaleReviewStage.TermsReviewed, flow.ReviewListing(12));
            Assert.IsTrue(flow.CanConfirm(12), "Only a separate action after terms review may close.");
            Assert.IsFalse(flow.CanConfirm(13), "Confirmation cannot be carried to another business.");

            flow.Reset();
            Assert.IsFalse(flow.CanConfirm(12));
        }

        // UBC A2: the player-path terms record who the seller is and what
        // cash the seller actually received — price plus pre-close cash
        // withdrawal — so the proceeds survive save/load with the business.
        [Test]
        public void SaleTerms_FullTerms_RoundTrip_PreservesSellerProceeds()
        {
            var terms = new BusinessSaleTerms(
                "Kennedy Baldwin-ooms",
                "Player",
                5400,
                "included as-is: 40 units of hardware",
                "buyer offers Ada Smith continued employment as Smith at 1200c/week",
                42,
                7,
                250000,
                30000,
                "Former owner holds no operator role after closing; no transition arrangement was agreed.");

            BusinessSaleTermsSaveDto dto = terms.CaptureSaveDto();
            BusinessSaleTerms restored = BusinessSaleTerms.FromSaveDto(dto);

            Assert.AreEqual(7, restored.SellerPersonId);
            Assert.AreEqual(250000, restored.PurchasePriceCents);
            Assert.AreEqual(30000, restored.SellerCashWithdrawalCents);
            Assert.AreEqual(280000, restored.SellerProceedsCents);
            Assert.AreEqual(
                "Former owner holds no operator role after closing; no transition arrangement was agreed.",
                restored.TransitionArrangement);
            Assert.AreEqual(5400, restored.IncludedCashCents);

            string summary = restored.BuildSummary();
            StringAssert.Contains("250000c", summary, "The price the seller was paid must be explicit.");
            StringAssert.Contains("30000c", summary, "The seller's cash withdrawal must be explicit.");
            StringAssert.Contains("280000c", summary, "Total seller proceeds must be explicit.");
            StringAssert.Contains("(P7)", summary, "The seller's person id must be explicit.");
            StringAssert.Contains("no transition arrangement was agreed", summary);
        }

        // UBC A2: the original six-argument terms (notably the to-Town path,
        // where no cash price changes hands) default to no seller identity
        // and no seller proceeds — never a fabricated price.
        [Test]
        public void SaleTerms_LegacySixArgTerms_DefaultToNoSellerProceeds()
        {
            BusinessSaleTerms terms = SampleTerms();
            var defaultTerms = new BusinessSaleTerms();

            Assert.AreEqual(-1, terms.SellerPersonId);
            Assert.AreEqual(0, terms.PurchasePriceCents);
            Assert.AreEqual(0, terms.SellerCashWithdrawalCents);
            Assert.AreEqual(0, terms.SellerProceedsCents);
            Assert.AreEqual(string.Empty, terms.TransitionArrangement);
            Assert.AreEqual(-1, defaultTerms.SellerPersonId);
            Assert.AreEqual(0, defaultTerms.SellerProceedsCents);
            Assert.AreEqual(string.Empty, defaultTerms.TransitionArrangement);
        }

        // UBC A2: full sale terms persist on the business record itself
        // through save/load (item 3 — the player path mirrors the to-Town
        // path, which already records its terms this way).
        [Test]
        public void BusinessInstance_FullSaleTerms_PersistThroughSaveLoad()
        {
            BusinessProfileDefinition profile =
                BusinessProfileDefinition.CreateFallback(BusinessType.Blacksmith, "Test Smithy");
            BusinessInstanceState business =
                BusinessInstanceState.Create("test_sale_terms_full", profile, 0, BusinessOwnerIdentity.Npc(7, "Kennedy Baldwin-ooms", "Baldwin-ooms"));

            business.RecordSaleTerms(new BusinessSaleTerms(
                "Kennedy Baldwin-ooms",
                "Player",
                5400,
                "included as-is: 40 units of hardware",
                "no workers employed at closing",
                42,
                7,
                250000,
                30000,
                "Former owner holds no operator role after closing; no transition arrangement was agreed."));

            BusinessInstanceSaveDto dto = business.CaptureSaveDto();
            BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(dto);

            Assert.IsNotNull(restored);
            Assert.AreEqual(7, restored.SaleTerms.SellerPersonId);
            Assert.AreEqual(250000, restored.SaleTerms.PurchasePriceCents);
            Assert.AreEqual(30000, restored.SaleTerms.SellerCashWithdrawalCents);
            Assert.AreEqual(280000, restored.SaleTerms.SellerProceedsCents);
        }
    }
}
