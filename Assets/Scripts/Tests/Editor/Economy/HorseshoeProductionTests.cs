using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D1 (brief §18 H-L): one horseshoe is the manufacturing unit and the
    /// inventory unit. One shoe is one actual item; four separate shoes
    /// assemble into a four-hoof service order; one-shoe replacement needs
    /// no batch; forge inputs are conserved through the quarter pools;
    /// labour and skill affect production. Forging never fits a shoe —
    /// fitting is FarrierService.FitShoes (see HorseshoeFittingTests).
    /// </summary>
    [TestFixture]
    public sealed class HorseshoeProductionTests
    {
        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private static BlacksmithRuntime NewSmithy(int iron = 100, int coal = 50, bool forgeReady = true)
        {
            var smithy = new BlacksmithRuntime("smith-1", "Test Smithy", forgeReady);
            if (iron > 0)
            {
                smithy.ReceiveMaterial(new ImportLot
                {
                    LotId = "IMP-TEST-1", MaterialId = ImportCatalog.IronStockId,
                    MaterialName = "Iron stock", Units = iron,
                    OriginName = "Pittsburgh ironworks, via railhead", OrderId = "IMP-ORD-1",
                }, new List<string>());
            }
            if (coal > 0)
            {
                smithy.ReceiveMaterial(new ImportLot
                {
                    LotId = "IMP-TEST-2", MaterialId = ImportCatalog.ForgeCoalId,
                    MaterialName = "Forge coal", Units = coal,
                    OriginName = "Off-map coal dealer", OrderId = "IMP-ORD-2",
                }, new List<string>());
            }
            return smithy;
        }

        private static SkillService SkilledSmith(EntityId smith, string skillId, int minutes)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            skills.GrantPractice(smith, skillId, minutes);
            return skills;
        }

        /// <summary>Novice farrier capability: no practice recorded (capability 0.10).</summary>
        private static SkillService NoviceFarrier(EntityId smith)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            return skills;
        }

        // H: one shoe is one actual item.
        [Test]
        public void Forge_OneShoe_CreatesOneActualItem()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceFarrier(smith);
            var diagnostics = new List<string>();
            int minutes;

            EquipmentAsset shoe = smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes);

            Assert.IsNotNull(shoe, string.Join("; ", diagnostics));
            Assert.AreEqual("horseshoe", shoe.Kind);
            Assert.AreEqual("Horseshoe", shoe.DisplayName);
            Assert.AreEqual(1, smithy.Assets.Count, "one shoe is one item in shop stock");
            Assert.AreEqual("smith-1", shoe.MadeByBusinessId);
            Assert.AreEqual(42, shoe.MadeDayIndex);
            // One quarter of each unit spent; the rest waits as cut quarters.
            Assert.AreEqual(100 - 1, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(50 - 1, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.AreEqual(3, smithy.IronShoeQuartersOnHand);
            Assert.AreEqual(3, smithy.CoalShoeQuartersOnHand);
            Assert.AreEqual(28, minutes, "novice (capability 0.10) forges one shoe in 28 minutes");
            Assert.AreEqual(0.64f, shoe.Condition01);
        }

        // I + K: four separate shoes assemble into a four-hoof order, inputs conserved.
        [Test]
        public void Forge_FourShoes_ConsumeExactlyOneIronAndOneCoal()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceFarrier(smith);
            var diagnostics = new List<string>();
            var ids = new HashSet<string>();

            for (int i = 0; i < 4; i++)
            {
                int minutes;
                EquipmentAsset shoe = smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes);
                Assert.IsNotNull(shoe, string.Join("; ", diagnostics));
                Assert.IsTrue(ids.Add(shoe.AssetId), "each shoe is a distinct item");
            }

            Assert.AreEqual(4, smithy.Assets.Count, "four separate shoes, not one set");
            Assert.AreEqual(100 - 1, smithy.MaterialOnHand(ImportCatalog.IronStockId), "four shoes consume one iron unit");
            Assert.AreEqual(50 - 1, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "four shoes consume one coal unit");
            Assert.AreEqual(0, smithy.IronShoeQuartersOnHand);
            Assert.AreEqual(0, smithy.CoalShoeQuartersOnHand);
        }

        // K: conservation across a second unit boundary.
        [Test]
        public void Forge_EightShoes_InputsConserved()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceFarrier(smith);
            var diagnostics = new List<string>();

            for (int i = 0; i < 8; i++)
            {
                int minutes;
                Assert.IsNotNull(smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes));
            }

            Assert.AreEqual(8, smithy.Assets.Count);
            Assert.AreEqual(100 - 2, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(50 - 2, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.AreEqual(0, smithy.IronShoeQuartersOnHand);
            Assert.AreEqual(0, smithy.CoalShoeQuartersOnHand);
        }

        // K: the fifth shoe draws a second unit and leaves three quarters.
        [Test]
        public void Forge_FiveShoes_QuarterPoolArithmetic()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceFarrier(smith);
            var diagnostics = new List<string>();

            for (int i = 0; i < 5; i++)
            {
                int minutes;
                Assert.IsNotNull(smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes));
            }

            Assert.AreEqual(5, smithy.Assets.Count);
            Assert.AreEqual(100 - 2, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(50 - 2, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.AreEqual(3, smithy.IronShoeQuartersOnHand);
            Assert.AreEqual(3, smithy.CoalShoeQuartersOnHand);
        }

        [Test]
        public void Forge_RefusesWithoutIron_ConsumesNothing()
        {
            var smithy = NewSmithy(iron: 0, coal: 50);
            EntityId smith = Person(1);
            var skills = NoviceFarrier(smith);
            var diagnostics = new List<string>();
            int minutes = 99;

            EquipmentAsset shoe = smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes);

            Assert.IsNull(shoe);
            Assert.AreEqual(0, minutes, "no work done, no labour spent");
            Assert.AreEqual(50, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "no coal consumed on refusal");
            Assert.AreEqual(0, smithy.Assets.Count);
        }

        [Test]
        public void Forge_RefusesWithoutCoal_ConsumesNothing()
        {
            var smithy = NewSmithy(iron: 100, coal: 0);
            EntityId smith = Person(1);
            var skills = NoviceFarrier(smith);
            var diagnostics = new List<string>();
            int minutes;

            Assert.IsNull(smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes));
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId), "no iron consumed on refusal");
            Assert.AreEqual(0, smithy.Assets.Count);
        }

        [Test]
        public void Forge_RefusesWithoutForgeStation()
        {
            var smithy = NewSmithy(forgeReady: false);
            EntityId smith = Person(1);
            var skills = SkilledSmith(smith, BlacksmithRuntime.FarrierSkillId, 900);
            var diagnostics = new List<string>();
            int minutes;

            Assert.IsNull(smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes));
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(50, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
        }

        [Test]
        public void Forge_RefusesWithoutSkillService()
        {
            var smithy = NewSmithy();
            var diagnostics = new List<string>();
            int minutes;

            Assert.IsNull(smithy.ForgeHorseshoe(Person(1), 42, null, diagnostics, out minutes));
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId));
        }

        // L: labour and skill affect production — novice is slow and never perfect.
        [Test]
        public void Forge_SkillAffectsMinutesAndQuality_Novice()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceFarrier(smith); // capability 0.10
            var diagnostics = new List<string>();
            int minutes;

            EquipmentAsset shoe = smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes);

            Assert.IsNotNull(shoe);
            Assert.AreEqual(28, minutes);
            Assert.AreEqual(0.64f, shoe.Condition01);
            Assert.IsTrue(shoe.Condition01 < 1f, "a low-skill attempt never produces a guaranteed perfect shoe");
        }

        // L: mid capability is faster and better; mastery is fastest and perfect.
        [Test]
        public void Forge_SkillAffectsMinutesAndQuality_MidAndMaster()
        {
            var smithy = NewSmithy();
            var diagnostics = new List<string>();

            EntityId mid = Person(2);
            var midSkills = SkilledSmith(mid, BlacksmithRuntime.FarrierSkillId, 400); // capability 4/9
            int midMinutes;
            EquipmentAsset midShoe = smithy.ForgeHorseshoe(mid, 42, midSkills, diagnostics, out midMinutes);
            Assert.IsNotNull(midShoe, string.Join("; ", diagnostics));
            Assert.AreEqual(21, midMinutes);
            Assert.AreEqual(0.78f, midShoe.Condition01);

            EntityId master = Person(3);
            var masterSkills = SkilledSmith(master, BlacksmithRuntime.FarrierSkillId, 900); // capability 1.0
            int masterMinutes;
            EquipmentAsset masterShoe = smithy.ForgeHorseshoe(master, 42, masterSkills, diagnostics, out masterMinutes);
            Assert.IsNotNull(masterShoe, string.Join("; ", diagnostics));
            Assert.AreEqual(10, masterMinutes);
            Assert.AreEqual(1f, masterShoe.Condition01);
        }

        // The generic whole-unit path cannot mint quarter-priced shoes.
        [Test]
        public void CraftEquipment_RefusesHorseshoe_FractionalRecipe()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = SkilledSmith(smith, BlacksmithRuntime.FarrierSkillId, 900); // journeyman-level
            var diagnostics = new List<string>();

            EquipmentAsset shoe = smithy.CraftEquipment("horseshoe", smith, 42, skills, diagnostics);

            Assert.IsNull(shoe, "quarter-priced recipes are forged one at a time through ForgeHorseshoe");
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(50, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.IsTrue(diagnostics.Exists(d => d.Contains("ForgeHorseshoe")));
        }

        // The retired four-shoe recipe no longer exists.
        [Test]
        public void CraftEquipment_RefusesHorseshoeSet_NoRecipe()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = SkilledSmith(smith, BlacksmithRuntime.FarrierSkillId, 900);
            var diagnostics = new List<string>();

            Assert.IsNull(smithy.CraftEquipment("horseshoe-set", smith, 42, skills, diagnostics));
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId));
        }

        [Test]
        public void TakeHorseshoe_ReleasesOneShoe()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceFarrier(smith);
            var diagnostics = new List<string>();
            int minutes;
            EquipmentAsset first = smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes);
            EquipmentAsset second = smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes);
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);

            EquipmentAsset taken = smithy.TakeHorseshoe(first.AssetId, diagnostics);

            Assert.IsNotNull(taken);
            Assert.AreEqual(first.AssetId, taken.AssetId);
            Assert.AreEqual(1, smithy.Assets.Count);
            Assert.IsTrue(smithy.Assets.ContainsKey(second.AssetId), "the other shoe stays in stock");
        }

        [Test]
        public void TakeHorseshoe_RefusesUnknownId_NonShoe_Reserved()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceFarrier(smith);
            var diagnostics = new List<string>();
            int minutes;
            EquipmentAsset shoe = smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes);
            Assert.IsNotNull(shoe);

            Assert.IsNull(smithy.TakeHorseshoe("no-such-asset", diagnostics));
            Assert.AreEqual(1, smithy.Assets.Count);

            // A plow is not a horseshoe.
            var plowSkills = SkilledSmith(smith, BlacksmithRuntime.SmithingSkillId, 100);
            EquipmentAsset plow = smithy.CraftEquipment("plow", smith, 42, plowSkills, diagnostics);
            Assert.IsNotNull(plow);
            Assert.IsNull(smithy.TakeHorseshoe(plow.AssetId, diagnostics));
            Assert.IsTrue(smithy.Assets.ContainsKey(plow.AssetId));

            // A reserved shoe cannot be taken out from under its reservation.
            Assert.IsNull(shoe.Reserve("farrier-1", "fitting"));
            Assert.IsNull(smithy.TakeHorseshoe(shoe.AssetId, diagnostics));
            Assert.IsTrue(smithy.Assets.ContainsKey(shoe.AssetId));
        }
    }
}
