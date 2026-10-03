using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Trade;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// EQU-2: the blacksmith as a real business — fabrication from imported materials
    /// and repair work orders fed by real demand. Canon §7.2: skill-dependent,
    /// queue-driven, priced labor + parts, never free.
    /// </summary>
    [TestFixture]
    public sealed class BlacksmithTests
    {
        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private static BlacksmithRuntime NewSmithy(bool forgeReady = true)
        {
            var smithy = new BlacksmithRuntime("smith-1", "Test Smithy", forgeReady);
            // Stock: imported iron + coal, as EQU-1 would deliver.
            smithy.ReceiveMaterial(new ImportLot
            {
                LotId = "IMP-TEST-1", MaterialId = ImportCatalog.IronStockId,
                MaterialName = "Iron stock", Units = 100,
                OriginName = "Pittsburgh ironworks, via railhead", OrderId = "IMP-ORD-1",
            }, new List<string>());
            smithy.ReceiveMaterial(new ImportLot
            {
                LotId = "IMP-TEST-2", MaterialId = ImportCatalog.ForgeCoalId,
                MaterialName = "Forge coal", Units = 50,
                OriginName = "Off-map coal dealer", OrderId = "IMP-ORD-2",
            }, new List<string>());
            return smithy;
        }

        private static SkillService SkilledSmith(EntityId smith, string skillId, int minutes)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            skills.GrantPractice(smith, skillId, minutes);
            return skills;
        }

        [Test]
        public void Craft_ProducesAsset_WithProvenance()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = SkilledSmith(smith, BlacksmithRuntime.SmithingSkillId, 100); // level 2
            var diagnostics = new List<string>();

            EquipmentAsset plow = smithy.CraftEquipment("plow", smith, 42, skills, diagnostics);

            Assert.IsNotNull(plow, string.Join("; ", diagnostics));
            Assert.AreEqual("plow", plow.Kind);
            Assert.AreEqual(1f, plow.Condition01);
            Assert.AreEqual("smith-1", plow.MadeByBusinessId);
            Assert.AreEqual(42, plow.MadeDayIndex);
            Assert.AreEqual(100 - 8, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(50 - 2, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
        }

        [Test]
        public void Craft_Refused_WithoutQualifiedSmith()
        {
            var smithy = NewSmithy();
            EntityId apprentice = Person(2);
            var skills = SkilledSmith(apprentice, BlacksmithRuntime.SmithingSkillId, 10); // level 1
            var diagnostics = new List<string>();

            EquipmentAsset plow = smithy.CraftEquipment("plow", apprentice, 42, skills, diagnostics);

            Assert.IsNull(plow, "without a journeyman the work collapses (Canon §7.2)");
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId), "no materials consumed");
        }

        [Test]
        public void Craft_Refused_WithoutForgeStation()
        {
            var smithy = NewSmithy(forgeReady: false);
            EntityId smith = Person(3);
            var skills = SkilledSmith(smith, BlacksmithRuntime.SmithingSkillId, 500);
            var diagnostics = new List<string>();

            Assert.IsNull(smithy.CraftEquipment("plow", smith, 42, skills, diagnostics));
        }

        [Test]
        public void Repair_Lifecycle_Completes_And_BooksRevenue()
        {
            var smithy = NewSmithy();
            var diagnostics = new List<string>();
            var queue = smithy.Repairs;

            RepairWorkOrder order = queue.Intake("Farmer Jo", "farm-1", "EQ-farm-1-001",
                "Moldboard plow", "share chipped", "smithing", "farm-1", RepairUrgency.Routine, 50, diagnostics);
            Assert.IsNull(queue.Diagnose(order.WorkOrderId, "share chipped through, needs replacement",
                2, ImportCatalog.IronStockId, 90));
            Assert.IsNull(queue.AgreeTerms(order.WorkOrderId, 350));

            var asset = new EquipmentAsset { AssetId = "EQ-farm-1-001", Kind = "plow", Condition01 = 0.3f };
            EntityId smith = Person(4);
            var skills = SkilledSmith(smith, BlacksmithRuntime.SmithingSkillId, 200);
            var customerLedger = new HouseholdLedger(9);
            customerLedger.RecordInflow(50, 1000, HouseholdIncomeSource.OtherDocumented,
                "test", "test funds", "test");

            string failure = smithy.CompleteRepair(order.WorkOrderId, asset, smith, 51,
                skills, customerLedger, diagnostics);

            Assert.IsNull(failure, failure);
            Assert.AreEqual(RepairOrderStatus.Complete, order.Status);
            Assert.AreEqual(1f, asset.Condition01);
            Assert.AreEqual(350, smithy.RepairRevenueCents);
            Assert.AreEqual(1000 - 350, customerLedger.GetBalanceCents());
        }

        [Test]
        public void Asset_CannotBeDoubleBooked()
        {
            var asset = new EquipmentAsset { AssetId = "EQ-1", Kind = "plow" };
            Assert.IsNull(asset.Reserve("task-a", "plowing field 3"));
            string conflict = asset.Reserve("task-b", "plowing field 4");
            Assert.IsNotNull(conflict, "one reservation authority (Tech X §3.8)");
            asset.Release();
            Assert.IsNull(asset.Reserve("task-b", "plowing field 4"));
        }
    }
}
