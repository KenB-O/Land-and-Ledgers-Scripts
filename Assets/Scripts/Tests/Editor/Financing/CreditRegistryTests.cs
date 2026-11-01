using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Financing
{
    /// <summary>
    /// T2A: banking & credit — the five instruments stay structurally distinct,
    /// contingent guaranty exposure stays off the principal books until called,
    /// and the Tech X §12.2 Bakery Buyout fixture behaves exactly as specified.
    /// </summary>
    public sealed class CreditRegistryTests
    {
        private EntityIdRegistry ids;
        private CreditRegistry registry;
        private BusinessLiabilityLedger ledger;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            ids = new EntityIdRegistry();
            registry = new CreditRegistry();
            ledger = new BusinessLiabilityLedger();
            diag = new List<string>();
        }

        [Test]
        public void InstrumentsAreStructurallyDistinctWithUniqueIds()
        {
            var note = registry.IssuePromissoryNote(ids, "Abel", "Bank", 500000, "8%, due day 900", 10, null, diag);
            var seller = registry.IssueSellerFinanceNote(ids, "Baker", "Abel", "bakery", "biz-bakery", 800000, 300000, "10%, 5 years", 11, diag);
            var mortgage = registry.IssueMortgage(ids, "Abel", "Bank", "prop-1", "lot 4", 400000, "7%, 10 years", 12, diag);
            var lien = registry.FileLien(ids, "Smith", "Abel", "prop-1", 50000, "unpaid lumber", 13, diag);
            var guaranty = registry.IssueGuaranty(ids, "Player", "Bank", "Abel", note.InstrumentId.ToString(), 10000, "pay on default", 14, diag);

            Assert.AreEqual(CreditInstrumentKind.PromissoryNote, ((ICreditInstrument)note).Kind);
            Assert.AreEqual(CreditInstrumentKind.SellerFinanceNote, ((ICreditInstrument)seller).Kind);
            Assert.AreEqual(CreditInstrumentKind.Mortgage, ((ICreditInstrument)mortgage).Kind);
            Assert.AreEqual(CreditInstrumentKind.Lien, ((ICreditInstrument)lien).Kind);
            Assert.AreEqual(CreditInstrumentKind.Guaranty, ((ICreditInstrument)guaranty).Kind);
            Assert.AreEqual(5, registry.InstrumentCount);

            // Seller-finance math is structural: financed = price - down.
            Assert.AreEqual(500000, seller.FinancedCents);
            // A lien does not transfer possession — it is a claim.
            Assert.AreEqual(CreditInstrumentStatus.Active, ((ICreditInstrument)lien).Status);
        }

        [Test]
        public void AnonymousPartiesAreRefused()
        {
            var g = registry.IssueGuaranty(ids, "", "Bank", "Abel", "note-1", 10000, "terms", 10, diag);
            Assert.IsNull(g);
            var m = registry.IssueMortgage(ids, "Abel", "Bank", "", "lot 4", 400000, "terms", 10, diag);
            Assert.IsNull(m, "A mortgage with no property is refused — it is secured by real property or it is not a mortgage.");
        }

        [Test]
        public void BakeryBuyout_GuarantyStaysContingentUntilDefault()
        {
            // The buyer is financed by a third-party lender; the player
            // guarantees up to $100 (10000c) of the buyer's debt.
            var buyerNote = registry.IssuePromissoryNote(ids, "Buyer", "Frontier Bank", 500000, "8%, due day 900", 20, null, diag);
            var guaranty = registry.IssueGuaranty(ids, "Player", "Frontier Bank", "Buyer",
                buyerNote.InstrumentId.ToString(), 10000, "pay on buyer default, up to $100", 21, diag);

            // After closing: player ownership is zero and seller-note is zero by
            // construction of the sale (no seller finance issued here); the
            // player's PRINCIPAL debt is zero...
            Assert.AreEqual(0, ledger.TotalLiabilitiesFor("player-biz"));
            // ...while contingent guaranteed exposure remains $100.
            Assert.AreEqual(10000, registry.ContingentExposureFor("Player"));
            Assert.AreEqual(0, registry.CalledExposureFor("Player"));

            // Trigger buyer default: the guaranty transitions THROUGH its
            // agreement rather than disappearing.
            registry.RecordDefault(buyerNote.InstrumentId.ToString(), 500000, 30,
                ledger, ids, "player-biz", diag);

            Assert.AreEqual(CreditInstrumentStatus.Called, ((ICreditInstrument)guaranty).Status);
            Assert.AreEqual(10000, guaranty.CalledAmountCents, "Call is capped at the guaranty's max exposure.");
            Assert.AreEqual(0, registry.ContingentExposureFor("Player"), "Called exposure is no longer contingent.");
            Assert.AreEqual(10000, registry.CalledExposureFor("Player"));
            Assert.AreEqual(10000, ledger.TotalLiabilitiesFor("player-biz"),
                "The called guaranty is now a real liability on the player's books.");
        }

        [Test]
        public void SatisfiedDebtReleasesTheGuarantyExplicitly()
        {
            var note = registry.IssuePromissoryNote(ids, "Buyer", "Bank", 500000, "terms", 20, null, diag);
            registry.IssueGuaranty(ids, "Player", "Bank", "Buyer", note.InstrumentId.ToString(), 10000, "terms", 21, diag);
            Assert.AreEqual(10000, registry.ContingentExposureFor("Player"));

            registry.SatisfyInstrument(note.InstrumentId.ToString(), diag);

            Assert.AreEqual(0, registry.ContingentExposureFor("Player"),
                "The guaranty is released through an explicit terminal state, not silently dropped.");
        }

        [Test]
        public void EquipmentCanCollateralizeANote()
        {
            var note = registry.IssuePromissoryNote(ids, "Abel", "Bank", 500000, "8%, due day 900", 10,
                new List<string> { "equip-plow-001", "equip-wagon-004" }, diag);
            Assert.IsNotNull(note);
            CollectionAssert.AreEqual(
                new[] { "equip-plow-001", "equip-wagon-004" },
                note.CollateralEquipmentAssetIds);
        }

        [Test]
        public void SaveLoadRoundTripsInstrumentsWithStatus()
        {
            var note = registry.IssuePromissoryNote(ids, "Buyer", "Bank", 500000, "terms", 20, null, diag);
            var g = registry.IssueGuaranty(ids, "Player", "Bank", "Buyer", note.InstrumentId.ToString(), 10000, "terms", 21, diag);
            registry.RecordDefault(note.InstrumentId.ToString(), 500000, 30, null, null, null, diag);

            var dto = registry.CaptureSaveDto();
            var restored = new CreditRegistry();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(2, restored.InstrumentCount);
            Assert.AreEqual(CreditInstrumentStatus.Called,
                ((ICreditInstrument)restored.CaptureSaveDto().Guaranties[0]).Status);
            Assert.AreEqual(10000, restored.CalledExposureFor("Player"));
            Assert.AreEqual(0, restored.ContingentExposureFor("Player"));
        }
    }
}
