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
    }
}
