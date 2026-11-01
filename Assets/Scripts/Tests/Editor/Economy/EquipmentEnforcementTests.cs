using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// NX-1A: equipment enforcement — Canon 4.1 CANON LOCK ("a task is
    /// executable only when the relevant combination exists"). The gate refuses
    /// loudly with the named missing requirement; never a silent debuff
    /// (Tech X §3.9). Tests the TaskAuthority.StartTask choke point, the
    /// OR-alternative syntax (Canon 4.7), borrowed-equipment grants
    /// (Tech X §3.7), and workstation supportability layering (Canon 5.2).
    /// </summary>
    [TestFixture]
    public sealed class EquipmentEnforcementTests
    {
        private sealed class Fixture
        {
            public TaskAuthority Authority = new TaskAuthority();
            public List<EquipmentAsset> Assets = new List<EquipmentAsset>();
            public List<ToolKitInstance> Kits = new List<ToolKitInstance>();
            public EquipmentAccessResolver AccessResolver = new EquipmentAccessResolver();
            public SupportContext Support = new SupportContext();
            public EquipmentTaskGate Gate;

            public Fixture()
            {
                Gate = new EquipmentTaskGate
                {
                    AssetsSupplier = () => Assets,
                    KitsSupplier = () => Kits,
                    AccessResolver = AccessResolver,
                    SupportSupplier = () => Support,
                };
                Authority.SetExecutionGate(Gate);
            }

            public static EquipmentAsset Asset(string kind, string ownerKind, string ownerId, float condition = 0.9f) =>
                new EquipmentAsset
                {
                    AssetId = "asset-" + kind + "-" + ownerId,
                    Kind = kind,
                    OwnerKind = ownerKind,
                    OwnerId = ownerId,
                    Condition01 = condition,
                };

            public TaskDefinition DefWith(params string[] codes)
            {
                var def = new TaskDefinition("nx1a-task", "NX-1A task", 10);
                foreach (string c in codes) def.EquipmentClasses.Add(c);
                string ignored;
                Authority.RegisterDefinition(def, out ignored);
                return def;
            }

            public WorkTask CreateAndAssign(string definitionId, int businessId = 7)
            {
                var owner = EntityId.For(EntityKind.Business, businessId);
                WorkTask task = Authority.CreateTask(definitionId, owner, 0);
                var worker = EntityId.For(EntityKind.Person, 3);
                string rejection;
                Assert.IsTrue(Authority.AssignTask(task.TaskId, worker, null, out rejection),
                    "AssignTask should succeed in fixture: " + rejection);
                return task;
            }
        }

        [Test]
        public void StartTask_RefusedWhenEquipmentMissing_NamesRequirement()
        {
            var f = new Fixture();
            f.DefWith(EquipmentRequirementCodes.Asset("moldboard-plow"));
            WorkTask task = f.CreateAndAssign("nx1a-task");

            string rejection;
            bool started = f.Authority.StartTask(task.TaskId, 0, out rejection);

            Assert.IsFalse(started, "Plow-field must not start with no plow (Canon 4.1).");
            Assert.IsTrue(rejection.Contains("moldboard-plow"),
                "Refusal must name the missing requirement, not fail silently: " + rejection);
        }

        [Test]
        public void StartTask_ProceedsWhenEquipmentUsable()
        {
            var f = new Fixture();
            f.DefWith(EquipmentRequirementCodes.Asset("moldboard-plow"));
            f.Assets.Add(Fixture.Asset("moldboard-plow", "business", "7"));
            WorkTask task = f.CreateAndAssign("nx1a-task");

            string rejection;
            bool started = f.Authority.StartTask(task.TaskId, 0, out rejection);

            Assert.IsTrue(started, "Usable plow satisfies the gate: " + rejection);
        }

        [Test]
        public void StartTask_RefusedWhenEquipmentBroken()
        {
            var f = new Fixture();
            f.DefWith(EquipmentRequirementCodes.Asset("moldboard-plow"));
            f.Assets.Add(Fixture.Asset("moldboard-plow", "business", "7", 0.01f));
            WorkTask task = f.CreateAndAssign("nx1a-task");

            string rejection;
            bool started = f.Authority.StartTask(task.TaskId, 0, out rejection);

            Assert.IsFalse(started, "Wrecked plow blocks work (Tech X §3.9) — condition gates, never debuffs.");
            Assert.IsTrue(rejection.Contains("moldboard-plow"), "Refusal names the requirement: " + rejection);
        }

        [Test]
        public void OrAlternatives_PassWithEitherMethod()
        {
            var f = new Fixture();
            f.DefWith(EquipmentRequirementCodes.Asset("scythe") + "|" + EquipmentRequirementCodes.Asset("reaper-binder"));
            // Only the mechanized alternative is held — Canon 4.7: one workable method suffices.
            f.Assets.Add(Fixture.Asset("reaper-binder", "business", "7"));
            WorkTask task = f.CreateAndAssign("nx1a-task");

            string rejection;
            bool started = f.Authority.StartTask(task.TaskId, 0, out rejection);

            Assert.IsTrue(started, "OR alternative satisfied: " + rejection);
        }

        [Test]
        public void OrAlternatives_RefuseNamesBothWhenNeitherHeld()
        {
            var f = new Fixture();
            f.DefWith(EquipmentRequirementCodes.Asset("scythe") + "|" + EquipmentRequirementCodes.Asset("reaper-binder"));
            WorkTask task = f.CreateAndAssign("nx1a-task");

            string rejection;
            bool started = f.Authority.StartTask(task.TaskId, 0, out rejection);

            Assert.IsFalse(started);
            Assert.IsTrue(rejection.Contains("scythe") && rejection.Contains("reaper-binder"),
                "Refusal must name the alternatives: " + rejection);
        }

        [Test]
        public void BorrowedEquipment_SatisfiesViaRecordedGrant()
        {
            var f = new Fixture();
            f.DefWith(EquipmentRequirementCodes.Asset("threshing-separator"));
            // Separator owned by business 9, borrowed by business 7 via recorded grant (Tech X §3.7).
            f.Assets.Add(Fixture.Asset("threshing-separator", "business", "9"));
            f.AccessResolver.AddGrant(new EquipmentAccessGrant
            {
                GrantId = "grant-1",
                EquipmentKind = "threshing-separator",
                HolderKind = "business",
                HolderId = "7",
                Custody = EquipmentCustodyKind.Borrowed,
                GranterName = "Business 9",
                ExpiryDayIndex = 30,
            });
            WorkTask task = f.CreateAndAssign("nx1a-task");

            string rejection;
            bool started = f.Authority.StartTask(task.TaskId, 0, out rejection);

            Assert.IsTrue(started, "Recorded grant satisfies the requirement (Tech X §3.7): " + rejection);
        }

        [Test]
        public void ExpiredGrant_DoesNotSatisfy()
        {
            var f = new Fixture();
            f.DefWith(EquipmentRequirementCodes.Asset("threshing-separator"));
            f.Assets.Add(Fixture.Asset("threshing-separator", "business", "9"));
            f.AccessResolver.AddGrant(new EquipmentAccessGrant
            {
                GrantId = "grant-1",
                EquipmentKind = "threshing-separator",
                HolderKind = "business",
                HolderId = "7",
                Custody = EquipmentCustodyKind.Borrowed,
                ExpiryDayIndex = 5, // expired on day 10
            });
            WorkTask task = f.CreateAndAssign("nx1a-task");

            string rejection;
            bool started = f.Authority.StartTask(task.TaskId, 10, out rejection);

            Assert.IsFalse(started, "Expired grants confer no access.");
        }

        [Test]
        public void ToolkitRequirement_GatesOnUsableKit()
        {
            var f = new Fixture();
            f.DefWith(EquipmentRequirementCodes.Kit("carpenter-hand-tool-kit"));
            f.Kits.Add(new ToolKitInstance
            {
                InstanceId = "kit-1",
                KitId = "carpenter-hand-tool-kit",
                OwnerKind = "business",
                OwnerId = "7",
                Condition01 = 0.8f,
            });
            WorkTask task = f.CreateAndAssign("nx1a-task");

            string rejection;
            Assert.IsTrue(f.Authority.StartTask(task.TaskId, 0, out rejection), "Usable kit: " + rejection);
        }

        [Test]
        public void UnknownPrefix_RefusedNeverAssumed()
        {
            var f = new Fixture();
            f.DefWith("plow"); // legacy free-text, no coded prefix
            WorkTask task = f.CreateAndAssign("nx1a-task");

            string rejection;
            bool started = f.Authority.StartTask(task.TaskId, 0, out rejection);

            Assert.IsFalse(started, "Uncoded requirements refuse (Tech X §3.10).");
        }

        [Test]
        public void NoGateInstalled_StartTaskBehavesAsBefore()
        {
            var authority = new TaskAuthority(); // no gate
            var def = new TaskDefinition("nx1a-plain", "Plain task", 10);
            def.EquipmentClasses.Add(EquipmentRequirementCodes.Asset("moldboard-plow"));
            string ignored;
            authority.RegisterDefinition(def, out ignored);
            var task = authority.CreateTask("nx1a-plain", EntityId.For(EntityKind.Business, 7), 0);
            string rejection;
            Assert.IsTrue(authority.AssignTask(task.TaskId, EntityId.For(EntityKind.Person, 3), null, out rejection));
            Assert.IsTrue(authority.StartTask(task.TaskId, 0, out rejection),
                "Null gate preserves legacy behavior: " + rejection);
        }
    }
}
