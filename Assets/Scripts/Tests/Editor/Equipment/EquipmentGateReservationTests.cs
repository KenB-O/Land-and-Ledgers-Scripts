using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.Economy.Equipment.Tests
{
    /// <summary>
    /// Pass IV Worker 2: the equipment task gate enforces instance-level
    /// availability through the unified authority — one plow cannot satisfy N
    /// simultaneous tasks.
    /// </summary>
    [TestFixture]
    public sealed class EquipmentGateReservationTests
    {
        private sealed class Fixture
        {
            public TaskAuthority Authority = new TaskAuthority();
            public EquipmentReservationService Reservations = new EquipmentReservationService();
            public List<EquipmentAsset> Assets = new List<EquipmentAsset>();
            public EquipmentTaskGate Gate;

            public Fixture()
            {
                Gate = new EquipmentTaskGate
                {
                    AssetsSupplier = () => Assets,
                    KitsSupplier = () => new List<ToolKitInstance>(),
                    AccessResolver = new EquipmentAccessResolver(),
                    ReservationsSupplier = () => Reservations,
                    NowGameSecondsSupplier = () => 0f,
                };
                Authority.SetExecutionGate(Gate);
            }

            public static EquipmentAsset Plow(string assetId, string ownerId) =>
                new EquipmentAsset
                {
                    AssetId = assetId,
                    Kind = "plow",
                    DisplayName = "Plow " + assetId,
                    OwnerKind = "business",
                    OwnerId = ownerId,
                    Condition01 = 0.9f,
                };

            public void RegisterPlowTask(string definitionId)
            {
                var def = new TaskDefinition(definitionId, "Plow a field", 120);
                def.EquipmentClasses.Add(EquipmentRequirementCodes.Asset("plow"));
                string rejection;
                Assert.IsTrue(Authority.RegisterDefinition(def, out rejection), rejection);
            }

            public WorkTask CreateAndAssign(string definitionId, int businessId)
            {
                var owner = EntityId.For(EntityKind.Business, businessId);
                WorkTask task = Authority.CreateTask(definitionId, owner, 0);
                var worker = EntityId.For(EntityKind.Person, 3);
                string rejection;
                Assert.IsTrue(Authority.AssignTask(task.TaskId, worker, null, out rejection),
                    "AssignTask should succeed: " + rejection);
                return task;
            }

            public string StartForTask(WorkTask task)
            {
                string rejection;
                return Authority.StartTask(task.TaskId, 0, out rejection) ? null : rejection;
            }
        }

        [Test]
        public void OnePlow_TwoTasks_SecondRefused()
        {
            var f = new Fixture();
            f.RegisterPlowTask("plow-field");
            f.Assets.Add(Fixture.Plow("plow-1", "7"));

            WorkTask first = f.CreateAndAssign("plow-field", 7);
            Assert.IsNull(f.StartForTask(first), "First task with the free plow must start.");

            // The first task's flow reserves the plow instance through the
            // unified authority (Worker 3's stage reservation path).
            var hold = new ReservationRequest("task:" + first.TaskId, "Plow field 7");
            hold.Items.Add(new ReservationItem(
                ReservationKeyConventions.Asset("plow-1"), 1f, 0f, 7200f));
            Assert.IsNull(f.Reservations.TryReserveAll(hold, out _));

            WorkTask second = f.CreateAndAssign("plow-field", 7);
            string refusal = f.StartForTask(second);
            Assert.IsNotNull(refusal, "ONE plow cannot satisfy two simultaneous tasks.");
            Assert.IsTrue(refusal.Contains("plow-1"),
                "Refusal must name the held instance: " + refusal);
        }

        [Test]
        public void TwoPlows_TwoTasks_BothProceed()
        {
            var f = new Fixture();
            f.RegisterPlowTask("plow-field");
            f.Assets.Add(Fixture.Plow("plow-1", "7"));
            f.Assets.Add(Fixture.Plow("plow-2", "7"));

            WorkTask first = f.CreateAndAssign("plow-field", 7);
            Assert.IsNull(f.StartForTask(first));

            var hold = new ReservationRequest("task:" + first.TaskId, "Plow field 7");
            hold.Items.Add(new ReservationItem(
                ReservationKeyConventions.Asset("plow-1"), 1f, 0f, 7200f));
            Assert.IsNull(f.Reservations.TryReserveAll(hold, out _));

            WorkTask second = f.CreateAndAssign("plow-field", 7);
            Assert.IsNull(f.StartForTask(second),
                "The second plow is free — the second task must start.");
        }

        [Test]
        public void Gate_HonorsAssetLevelReservedFlag()
        {
            var f = new Fixture();
            f.RegisterPlowTask("plow-field");
            var plow = Fixture.Plow("plow-1", "7");
            f.Assets.Add(plow);

            // DraftWorkUnitService reserves the implement at the asset level.
            Assert.IsNull(plow.Reserve("unit-team:driver-9", "plowing"));

            WorkTask task = f.CreateAndAssign("plow-field", 7);
            string refusal = f.StartForTask(task);
            Assert.IsNotNull(refusal, "Asset-level reservation must also block the gate.");
            Assert.IsTrue(refusal.Contains("plow-1"), "Refusal names the instance: " + refusal);
        }

        [Test]
        public void ReservationExpires_TaskProceedsAfterWindow()
        {
            var f = new Fixture();
            f.RegisterPlowTask("plow-field");
            f.Assets.Add(Fixture.Plow("plow-1", "7"));

            var hold = new ReservationRequest("task:earlier", "Morning plowing");
            hold.Items.Add(new ReservationItem(
                ReservationKeyConventions.Asset("plow-1"), 1f, 0f, 3600f));
            Assert.IsNull(f.Reservations.TryReserveAll(hold, out _));

            // The gate checks at now = 7200s (after the window).
            f.Gate.NowGameSecondsSupplier = () => 7200f;
            WorkTask task = f.CreateAndAssign("plow-field", 7);
            Assert.IsNull(f.StartForTask(task),
                "An expired reservation must not block later work.");
        }

        [Test]
        public void MotiveRequirement_RefusedWhenTeamInUse()
        {
            var f = new Fixture();
            var def = new TaskDefinition("haul-grain", "Haul grain", 120);
            def.EquipmentClasses.Add(EquipmentRequirementCodes.Motive("horse-team-1"));
            string rejection;
            Assert.IsTrue(f.Authority.RegisterDefinition(def, out rejection), rejection);

            var hold = new ReservationRequest("task:other", "Other hauling");
            hold.Items.Add(new ReservationItem(
                ReservationKeyConventions.MotivePower("horse-team-1"), 1f, 0f, 7200f));
            Assert.IsNull(f.Reservations.TryReserveAll(hold, out _));

            WorkTask task = f.CreateAndAssign("haul-grain", 7);
            string refusal = f.StartForTask(task);
            Assert.IsNotNull(refusal, "A motive source in use cannot satisfy a second task.");
            Assert.IsTrue(refusal.Contains("horse-team-1"), "Refusal names the motive source: " + refusal);
        }

        [Test]
        public void MotiveRequirement_PassesWhenFree()
        {
            var f = new Fixture();
            var def = new TaskDefinition("haul-grain", "Haul grain", 120);
            def.EquipmentClasses.Add(EquipmentRequirementCodes.Motive("horse-team-1"));
            string rejection;
            Assert.IsTrue(f.Authority.RegisterDefinition(def, out rejection), rejection);

            WorkTask task = f.CreateAndAssign("haul-grain", 7);
            Assert.IsNull(f.StartForTask(task), "A free motive source satisfies the requirement.");
        }

        [Test]
        public void MotiveRequirement_RefusedWithoutAuthority()
        {
            var f = new Fixture();
            f.Gate.ReservationsSupplier = null; // no authority: cannot verify
            var def = new TaskDefinition("haul-grain", "Haul grain", 120);
            def.EquipmentClasses.Add(EquipmentRequirementCodes.Motive("horse-team-1"));
            string rejection;
            Assert.IsTrue(f.Authority.RegisterDefinition(def, out rejection), rejection);

            WorkTask task = f.CreateAndAssign("haul-grain", 7);
            string refusal = f.StartForTask(task);
            Assert.IsNotNull(refusal, "Motive power is never assumed without an authority.");
        }

        [Test]
        public void TaskWithoutEquipmentCodes_UnaffectedByGate()
        {
            var f = new Fixture();
            var def = new TaskDefinition("sweep-floor", "Sweep the floor", 30);
            string rejection;
            Assert.IsTrue(f.Authority.RegisterDefinition(def, out rejection), rejection);

            WorkTask task = f.CreateAndAssign("sweep-floor", 7);
            Assert.IsNull(f.StartForTask(task), "Tasks with no equipment codes must start freely.");
        }
    }
}
