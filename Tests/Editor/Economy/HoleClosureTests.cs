using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Businesses.ImplementDealer;
using LandLedgers.Economy.Tannery;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// EQP-5: hole closure — the tannery (hides → leather, unblocking the saddler),
    /// mule/ox as first-class motive power, the FuelDealer profile, and the
    /// implement-dealer ↔ blacksmith maker-provenance link.
    /// </summary>
    [TestFixture]
    public sealed class HoleClosureTests
    {
        [Test]
        public void Tannery_BatchLifecycle_HidesToLeather()
        {
            var tannery = new TanneryRuntime("tan-1", "Test Tannery");
            var diagnostics = new List<string>();

            string refusal = tannery.StartBatch(
                "hide-lot-1", 100, "butcher biz_butcher_001, steer #412",
                50, "tanbark import IMP-77",
                "worker-9", 100, diagnostics);
            Assert.IsNull(refusal, "Hides + bark + worker — the batch starts.");

            // Early completion refuses: leather takes months.
            Assert.IsNull(tannery.CompleteBatch(tannery.Batches[0].BatchId, 150, new List<string>()),
                "Tanning takes months — no early completion.");

            TanneryRuntime.LeatherLot leather = tannery.CompleteBatch(
                tannery.Batches[0].BatchId, 100 + TanneryRuntime.TanningDaysPerBatch, diagnostics);
            Assert.IsNotNull(leather);
            Assert.AreEqual(100, leather.Lbs);
            StringAssert.Contains("biz_butcher_001", leather.HideSourceNote);
            StringAssert.Contains("IMP-77", leather.BarkSourceNote);
            StringAssert.Contains("tan-1", leather.TanneryBusinessId);
        }

        [Test]
        public void Tannery_NoBark_RefusesBatch()
        {
            var tannery = new TanneryRuntime("tan-2", "Test Tannery");
            string refusal = tannery.StartBatch(
                "hide-lot-2", 100, "butcher", 0, "",
                "worker-9", 100, new List<string>());
            Assert.IsNotNull(refusal, "No tannin, no leather — bark is a real input.");
        }

        [Test]
        public void Tannery_TanningYard_NeedsWater()
        {
            var def = WorkstationCatalog.TanningYard;
            Assert.IsTrue(def.CapabilitiesGranted.Contains("tan-hides"));

            var context = new SupportContext();
            var report = Supportability.Evaluate(def.SupportRequirements, context);
            Assert.IsFalse(report.Satisfied, "A tanyard without running water tans nothing.");
            context.AvailableInfrastructure.Add("running-water");
            context.AvailableOperatorSkills.Add("tanning");
            report = Supportability.Evaluate(def.SupportRequirements, context);
            Assert.IsTrue(report.Satisfied);
        }

        [Test]
        public void BusinessType_Tannery_IsDistinctTrade()
        {
            Assert.AreEqual(19, (int)BusinessType.Tannery,
                "Historical: the tanner stood between the butcher and the leatherworker — its own trade.");
        }

        [Test]
        public void Species_MuleOx_FirstClassMotivePower()
        {
            Assert.AreEqual(8, (int)AnimalSpecies.Mule);
            Assert.AreEqual(9, (int)AnimalSpecies.Ox);
        }

        [Test]
        public void FuelYard_Profile_ScalesAreTheHardGate()
        {
            Assert.IsTrue(FuelYardProfile.EquipmentKinds.Contains("platform-scale"),
                "Fuel is sold by weight/cord — the scale gates selling (feed-merchant analogy).");
            Assert.AreEqual("asset:platform-scale", FuelYardProfile.RequirementForTask("sell-fuel"));
            Assert.IsTrue(FuelYardProfile.SupplyLinks.Exists(l => l.input.Contains("slabs")),
                "Sawmill slabs/offcuts close the waste loop.");
        }

        [Test]
        public void Dealer_Sale_PreservesSmithMakerProvenance()
        {
            var ids = new EntityIdRegistry();
            var dealer = new ImplementDealer("dealer-1", "Test Implements", "town-1");
            var diagnostics = new List<string>();

            var model = new ImplementModel
            {
                ModelId = "plow-a", DisplayName = "Walking Plow", Kind = "moldboard-plow",
                PriceCents = 1500, SupplierName = "Smith & Sons",
                SupplierVia = "local blacksmith",
                MakerBusinessId = "smith-1", MakerBusinessName = "Smith & Sons",
            };
            Assert.IsNull(dealer.StockModel(model, 2, diagnostics));

            var sale = dealer.SellImplement(ids, "plow-a", "business", "farm-1", 500, 4, 200, diagnostics);
            Assert.IsNotNull(sale);
            Assert.AreEqual(1, dealer.SoldAssets.Count);
            EquipmentAsset asset = dealer.SoldAssets[0];
            Assert.AreEqual("smith-1", asset.MadeByBusinessId,
                "EQP-5: the TRUE maker survives the dealer — maker → dealer → buyer provenance.");
            Assert.AreEqual("Smith & Sons", asset.MadeByBusinessName);
            Assert.AreEqual("farm-1", asset.OwnerId);
        }
    }
}
