using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W8C: miners hired through the existing recruitment channels; the
    /// roster records role/shift/wage against real EmploymentRelationship
    /// ids; shift wages move through HouseholdLedger authorities (business
    /// outflow + worker inflow with employment provenance). Danger/attrition
    /// is canon-gated and intentionally NOT modeled (see W8 report).
    /// </summary>
    [TestFixture]
    public sealed class MineLaborRegisterTests
    {
        private static EntityId EmploymentId(EntityIdRegistry registry) =>
            registry.Allocate(EntityKind.EmploymentRelationship);

        private static EntityId PersonId(EntityIdRegistry registry) =>
            registry.Allocate(EntityKind.Person);

        [Test]
        public void OpenHiringEffort_RoutesThroughRecruitmentChannels()
        {
            var recruitment = new RecruitmentService();
            var register = new MineLaborRegister();
            var diag = new List<string>();

            RecruitmentEffort effort = register.OpenHiringEffort(recruitment, "biz-mine-1",
                MineRole.HardRockMiner, RecruitmentChannel.Handbill, 400, 7, diag);
            Assert.IsNotNull(effort);
            Assert.AreEqual("Hard-rock miner", effort.RoleDisplayName);
            Assert.AreEqual(RecruitmentChannel.Handbill, effort.Channel);

            RecruitmentEffort bad = register.OpenHiringEffort(recruitment, "biz-mine-1",
                MineRole.Unspecified, RecruitmentChannel.Handbill, 400, 7, diag);
            Assert.IsNull(bad, "unspecified role refused");
        }

        [Test]
        public void AssignMiner_RequiresRealEmploymentAndRole()
        {
            var registry = new EntityIdRegistry();
            var register = new MineLaborRegister();
            var diag = new List<string>();

            Assert.IsNull(register.AssignMiner(EntityId.Invalid, PersonId(registry),
                MineRole.Mucker, MineShift.Day, 150, 400, diag), "invented employment refused");

            MineCrewAssignment assignment = register.AssignMiner(EmploymentId(registry), PersonId(registry),
                MineRole.Mucker, MineShift.Day, 150, 400, diag);
            Assert.IsNotNull(assignment);
            Assert.AreEqual(MineRole.Mucker, assignment.Role);
            Assert.AreEqual(MineShift.Day, assignment.Shift);
            Assert.AreEqual(150, assignment.WagePerShiftCents);
            Assert.IsTrue(assignment.IsActive);

            Assert.IsNull(register.AssignMiner(EmploymentId(registry), assignment.PersonId,
                MineRole.HoistOperator, MineShift.Night, 175, 401, diag), "double assignment refused");
            Assert.AreEqual(1, register.ActiveAssignments().Count);
        }

        [Test]
        public void ReleaseMiner_EndsAssignmentButRetainsHistory()
        {
            var registry = new EntityIdRegistry();
            var register = new MineLaborRegister();
            var diag = new List<string>();
            EntityId person = PersonId(registry);
            register.AssignMiner(EmploymentId(registry), person, MineRole.PumpOperator, MineShift.Night, 165, 400, diag);

            Assert.IsNull(register.ReleaseMiner(person, 410));
            Assert.AreEqual(0, register.ActiveAssignments().Count);
            Assert.AreEqual(1, register.Assignments.Count, "ended history retained");
            Assert.AreEqual(410, register.Assignments[0].EndDayIndex);

            Assert.IsNotNull(register.ReleaseMiner(person, 411), "releasing twice refused");
        }

        [Test]
        public void ActiveCountByRole_AndShiftWageBill()
        {
            var registry = new EntityIdRegistry();
            var register = new MineLaborRegister();
            var diag = new List<string>();
            register.AssignMiner(EmploymentId(registry), PersonId(registry), MineRole.HardRockMiner, MineShift.Day, 200, 400, diag);
            register.AssignMiner(EmploymentId(registry), PersonId(registry), MineRole.HardRockMiner, MineShift.Night, 225, 400, diag);
            register.AssignMiner(EmploymentId(registry), PersonId(registry), MineRole.Mucker, MineShift.Day, 150, 400, diag);

            Assert.AreEqual(2, register.ActiveCountByRole(MineRole.HardRockMiner));
            Assert.AreEqual(1, register.ActiveCountByRole(MineRole.Mucker));
            Assert.AreEqual(0, register.ActiveCountByRole(MineRole.Assayer));
            Assert.AreEqual(575, register.ShiftWageBillCents());
        }

        [Test]
        public void SettleShiftWages_PaysBusinessOutflowAndWorkerInflow()
        {
            var registry = new EntityIdRegistry();
            var register = new MineLaborRegister();
            var diag = new List<string>();
            EntityId person = PersonId(registry);
            register.AssignMiner(EmploymentId(registry), person, MineRole.HardRockMiner, MineShift.Day, 200, 400, diag);

            var businessCash = new HouseholdLedger(50);
            var workerLedger = new HouseholdLedger(51);
            var settleDiag = new List<string>();
            int paid = register.SettleShiftWages(businessCash, pid => workerLedger, 410, "week 58", settleDiag);

            Assert.AreEqual(1, paid);
            Assert.AreEqual(-200, businessCash.GetBalanceCents(), "business outflow recorded");
            Assert.AreEqual(200, workerLedger.GetBalanceCents(), "worker wage inflow recorded with employment provenance");
        }

        [Test]
        public void SettleShiftWages_SkipsReleasedMiners()
        {
            var registry = new EntityIdRegistry();
            var register = new MineLaborRegister();
            var diag = new List<string>();
            EntityId person = PersonId(registry);
            register.AssignMiner(EmploymentId(registry), person, MineRole.Mucker, MineShift.Day, 150, 400, diag);
            register.ReleaseMiner(person, 405);

            var businessCash = new HouseholdLedger(50);
            int paid = register.SettleShiftWages(businessCash, null, 410, "week 58", diag);
            Assert.AreEqual(0, paid);
            Assert.AreEqual(0, businessCash.GetBalanceCents());
        }

        [Test]
        public void RoleDisplayNames_CoverCanonOccupations()
        {
            Assert.AreEqual("Hard-rock miner", MineLaborRegister.GetRoleDisplayName(MineRole.HardRockMiner));
            Assert.AreEqual("Mucker (mine laborer)", MineLaborRegister.GetRoleDisplayName(MineRole.Mucker));
            Assert.AreEqual("Hoist operator", MineLaborRegister.GetRoleDisplayName(MineRole.HoistOperator));
            Assert.AreEqual("Pump operator", MineLaborRegister.GetRoleDisplayName(MineRole.PumpOperator));
            Assert.AreEqual("Assayer", MineLaborRegister.GetRoleDisplayName(MineRole.Assayer));
            Assert.AreEqual("Prospector", MineLaborRegister.GetRoleDisplayName(MineRole.Prospector));
            Assert.AreEqual("Ore / stamp-mill worker", MineLaborRegister.GetRoleDisplayName(MineRole.OreMillWorker));
        }

        [Test]
        public void LaborTaskCatalog_RegistersCrewTasks()
        {
            var authority = new TaskAuthority();
            var diag = new List<string>();
            MineLaborRegister.MineLaborTaskCatalog.Register(authority, diag);

            Assert.IsNotNull(authority.GetDefinition(MineLaborRegister.MineLaborTaskCatalog.BreakOreTaskId));
            Assert.IsNotNull(authority.GetDefinition(MineLaborRegister.MineLaborTaskCatalog.MuckOreTaskId));
        }

        [Test]
        public void SaveRoundTrip_PreservesRoster()
        {
            var registry = new EntityIdRegistry();
            var state = MineRuntimeState.CreateDefault(LandLedgers.World.MineralResourceKind.Coal);
            var diag = new List<string>();
            EntityId person = PersonId(registry);
            EntityId employment = EmploymentId(registry);
            state.LaborRegister.AssignMiner(employment, person, MineRole.HoistOperator, MineShift.Night, 180, 400, diag);

            var dto = state.CaptureSaveDto();
            MineRuntimeState restored = MineRuntimeState.FromSaveDto(dto);

            Assert.AreEqual(1, restored.LaborRegister.Assignments.Count);
            MineCrewAssignment restored2 = restored.LaborRegister.Assignments[0];
            Assert.AreEqual(person, restored2.PersonId);
            Assert.AreEqual(employment, restored2.EmploymentId);
            Assert.AreEqual(MineRole.HoistOperator, restored2.Role);
            Assert.AreEqual(180, restored2.WagePerShiftCents);
            Assert.IsTrue(restored2.IsActive);
        }
    }
}
