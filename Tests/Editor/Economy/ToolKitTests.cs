using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// EQP-3: Group B toolkits — data-driven kits with canonical contents, and the
    /// equipment-requirement evaluator that binds them to task data (Canon 4.1
    /// CANON LOCK, Tech X §3.10). No occupation gates, no generic Tools=true flag.
    /// </summary>
    [TestFixture]
    public sealed class ToolKitTests
    {
        private static TaskDefinition TaskWith(params string[] equipmentClasses)
        {
            var task = new TaskDefinition("test-task", "Test task", 10);
            foreach (string code in equipmentClasses)
                task.EquipmentClasses.Add(code);
            return task;
        }

        private static ToolKitInstance Kit(string instanceId, string kitId, string ownerKind, string ownerId, float condition = 0.9f) =>
            new ToolKitInstance
            {
                InstanceId = instanceId, KitId = kitId,
                OwnerKind = ownerKind, OwnerId = ownerId, Condition01 = condition,
            };

        [Test]
        public void Catalog_AllNineKits_DefinedWithCanonicalContents()
        {
            var all = ToolKitCatalog.All;
            Assert.AreEqual(10, all.Count, "Group B+C: carpenter, mason, tailor, barber, doctor, wheelwright, stable, miner, farrier, ranch tack.");
            foreach (var def in all)
            {
                Assert.IsTrue(def.CanonicalContents.Count > 0, $"{def.KitId} must carry canonical contents (Tech X §3.4).");
                Assert.IsFalse(string.IsNullOrWhiteSpace(def.SourceNote), $"{def.KitId} must cite its source.");
            }
        }

        [Test]
        public void Catalog_TailorKit_SewingMachineNeverGates()
        {
            var tailor = ToolKitCatalog.TailorHandKit;
            Assert.IsFalse(tailor.CanonicalContents.Contains("sewing machine"),
                "Canon 4.7: the sewing machine is capacity/scale equipment — hand methods remain physically possible.");
            Assert.IsTrue(tailor.CanonicalContents.Contains("shears"));
        }

        [Test]
        public void Catalog_BarberKit_StropIsTheMaintenanceLoop()
        {
            var barber = ToolKitCatalog.BarberKit;
            Assert.IsTrue(barber.CanonicalContents.Contains("strop"),
                "Razors dull; the strop restores — it is part of the kit, not separate (Canon Part V: Barber).");
        }

        [Test]
        public void Requirement_UsableKit_SatisfiesTask()
        {
            var task = TaskWith(EquipmentRequirementCodes.Kit("barber-kit"));
            var availability = EquipmentRequirements.ForHolder("person", "p1", 10,
                new List<EquipmentAsset>(),
                new List<ToolKitInstance> { Kit("kit-1", "barber-kit", "person", "p1") },
                null);
            Assert.IsNull(EquipmentRequirements.CheckTaskRequirements(task, availability, new List<string>()));
        }

        [Test]
        public void Requirement_WreckedKit_RefusesTask()
        {
            var task = TaskWith(EquipmentRequirementCodes.Kit("doctor-bag"));
            var availability = EquipmentRequirements.ForHolder("person", "p2", 10,
                new List<EquipmentAsset>(),
                new List<ToolKitInstance> { Kit("kit-2", "doctor-bag", "person", "p2", 0.01f) },
                null);
            var diagnostics = new List<string>();
            string refusal = EquipmentRequirements.CheckTaskRequirements(task, availability, diagnostics);
            Assert.IsNotNull(refusal, "Wrecked kit blocks the task (Tech X §3.9) — never a silent debuff.");
        }

        [Test]
        public void Requirement_BorrowedKit_ViaGrant_SatisfiesTask()
        {
            var resolver = new EquipmentAccessResolver();
            resolver.AddGrant(new EquipmentAccessGrant
            {
                EquipmentKind = "mason-kit", HolderKind = "person", HolderId = "p3",
                Custody = EquipmentCustodyKind.Borrowed, GranterName = "master mason",
            });
            var task = TaskWith(EquipmentRequirementCodes.Kit("mason-kit"));
            var availability = EquipmentRequirements.ForHolder("person", "p3", 10,
                new List<EquipmentAsset>(),
                new List<ToolKitInstance> { Kit("kit-3", "mason-kit", "person", "master-mason") },
                resolver);
            Assert.IsNull(EquipmentRequirements.CheckTaskRequirements(task, availability, new List<string>()),
                "Tech X §3.7: legitimate borrowed access satisfies the requirement.");
        }

        [Test]
        public void Requirement_NoAccess_RefusesTask()
        {
            var task = TaskWith(EquipmentRequirementCodes.Kit("farrier-kit"));
            var availability = EquipmentRequirements.ForHolder("person", "p4", 10,
                new List<EquipmentAsset>(),
                new List<ToolKitInstance> { Kit("kit-4", "farrier-kit", "person", "someone-else") },
                null);
            Assert.IsNotNull(EquipmentRequirements.CheckTaskRequirements(task, availability, new List<string>()),
                "Someone else's kit is not yours — access is never invented (Tech X §3.7).");
        }

        [Test]
        public void Requirement_UnknownPrefix_RefusedNeverAssumed()
        {
            var task = TaskWith("mystery:anvil");
            var availability = EquipmentRequirements.ForHolder("person", "p5", 10,
                new List<EquipmentAsset>(), new List<ToolKitInstance>(), null);
            Assert.IsNotNull(EquipmentRequirements.CheckTaskRequirements(task, availability, new List<string>()),
                "Tech X §3.10: unknown requirement kinds are refused, never assumed.");
        }

        [Test]
        public void Requirement_AssetKind_SatisfiedByUsableAsset()
        {
            var task = TaskWith(EquipmentRequirementCodes.Asset("anvil"));
            var availability = EquipmentRequirements.ForHolder("business", "smith-1", 10,
                new List<EquipmentAsset>
                {
                    new EquipmentAsset { AssetId = "anvil-1", Kind = "anvil", Condition01 = 0.8f, OwnerKind = "business", OwnerId = "smith-1" },
                },
                new List<ToolKitInstance>(), null);
            Assert.IsNull(EquipmentRequirements.CheckTaskRequirements(task, availability, new List<string>()));
        }
    }
}
