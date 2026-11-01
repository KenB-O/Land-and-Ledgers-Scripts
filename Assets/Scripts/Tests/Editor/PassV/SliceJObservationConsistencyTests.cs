using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.PassV
{
    /// <summary>
    /// Pass V (Worker 4) slice J: the SAME person performs the SAME action while
    /// fully represented, off-screen, streamed-out, and legitimately off-map.
    /// Authoritative outcomes (wages, cash, person-time, task state) must be
    /// identical across all four observation modes. The mode only selects the
    /// presentation notification label; it never enters the simulation math.
    ///
    /// Actual Unity camera tests stay Codex-side; they are recorded in
    /// Docs/Pass5_Worker4_CodexHandoff.md.
    /// </summary>
    [TestFixture]
    public sealed class SliceJObservationConsistencyTests
    {
        private sealed class RecordingSink : IObservationSink
        {
            public readonly List<string> Events = new List<string>();
            public void Notify(ObservationMode mode, string eventName, string detail)
            {
                Events.Add($"{mode}:{eventName}:{detail}");
            }
        }

        private sealed class World
        {
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public HouseholdLedger Ledger;
            public EmploymentRelationshipRegistry Employments;
            public WageSettlementService Wages;
            public BusinessRuntimeState Business;
            public EntityId PersonId = EntityId.For(EntityKind.Person, 1);
            public int HouseholdId = 1;
            public const string EmploymentId = "emp-slicej-1";
        }

        private World BuildWorld()
        {
            var world = new World
            {
                Tasks = new TaskAuthority(),
                Budgets = new WorkTimeBudgetStore(),
                Ledger = new HouseholdLedger(1),
                Employments = new EmploymentRelationshipRegistry(),
                Wages = new WageSettlementService(),
            };

            world.Tasks.RegisterDefinition(new TaskDefinition("work-shift", "Work shift", 480), out _);

            world.Employments.Register(new EmploymentRelationship
            {
                Id = World.EmploymentId,
                EmployeePersonId = 1,
                EmployerBusinessId = "biz-slicej-1",
                RoleDisplayName = "Mill hand",
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(1200, "slice J"),
                StartDayIndex = 0,
            });

            world.Business = new BusinessRuntimeState();
            world.Business.BindBusinessIdentity("biz-slicej-1");
            world.Business.AdjustCashCents(100000, recordNetDelta: false);

            world.Ledger.RecordInflow(0, 5000, HouseholdIncomeSource.OtherDocumented,
                "opening", "opening household cash", "test");
            return world;
        }

        /// <summary>
        /// The canonical slice-J action: P1 works a full shift (person-time),
        /// the weekly payroll settles, the wage posts to the household ledger,
        /// and the household buys goods. Identical code path for every mode.
        /// </summary>
        private string RunCanonicalAction(World world, ObservationMode mode, RecordingSink sink, List<string> diag)
        {
            // 1. Person-time: a real work task through the single task authority.
            WorkTask task = world.Tasks.CreateTaskWithPlannedMinutes(
                "work-shift", world.PersonId, 6, 480, customerRef: "slice-j");
            string rejection;
            Assert.IsTrue(world.Tasks.AssignTask(task.TaskId, world.PersonId, world.Budgets, 6, out rejection),
                "assign: " + rejection);
            Assert.IsTrue(world.Tasks.StartTask(task.TaskId, 6, out rejection), "start: " + rejection);
Assert.IsTrue(world.Tasks.RecordWork(task.TaskId, 480, world.Budgets, 6, out rejection),
                "work: " + rejection);
            Assert.AreEqual(TaskStatus.Complete, world.Tasks.FindTask(task.TaskId).Status,
                "a fully-worked shift auto-completes");
            SimulationPresentationBridge.Notify(mode, "shift-completed", "P1 worked 480 min");

            // 2. Wages: weekly payroll through the wage authority, then the
            // household ledger (the ONE cash writer) records the inflow.
            string settle = world.Wages.SettleWeeklyPayroll(world.Business, world.Employments, 0, 6, diag);
            Assert.AreEqual("settled", settle);
            foreach (PayrollEntry entry in world.Wages.Events[world.Wages.Events.Count - 1].Entries)
            {
                if (entry.Paid)
                {
                    string wageRejection = world.Ledger.RecordWagePayment(
                        6, entry.EmploymentId, entry.PersonId, entry.AmountCents, "week 0");
                    Assert.IsNull(wageRejection, wageRejection);
                }
            }
            SimulationPresentationBridge.Notify(mode, "wages-paid", "week 0 payroll");

            // 3. Purchase: real outflow for goods.
            string buyRejection = world.Ledger.RecordOutflow(6, 800, "weekly groceries", "general store");
            Assert.IsNull(buyRejection, buyRejection);
            SimulationPresentationBridge.Notify(mode, "purchase", "groceries 800c");

            // Fingerprint of every authoritative outcome.
            WorkTimeBudget budget = world.Budgets.GetOrCreate(world.PersonId);
            return string.Join("|",
                "ledger=" + world.Ledger.GetBalanceCents(),
                "entries=" + world.Ledger.Entries.Count,
                "worked=" + budget.MinutesWorked,
                "committed=" + budget.MinutesCommitted,
                "task=" + world.Tasks.FindTask(task.TaskId).Status,
                "bizCash=" + world.Business.CurrentCashCents,
                "sinkEvents=" + sink.Events.Count);
        }

        [Test]
        public void SameAction_AllFourModes_IdenticalAuthoritativeOutcomes()
        {
            var fingerprints = new Dictionary<ObservationMode, string>();
            var modes = new[]
            {
                ObservationMode.FullyRepresented,
                ObservationMode.OffScreen,
                ObservationMode.StreamedOut,
                ObservationMode.OffMap,
            };

            foreach (ObservationMode mode in modes)
            {
                SimulationPresentationBridge.ClearSinks();
                var sink = new RecordingSink();
                SimulationPresentationBridge.AttachSink(sink);
                try
                {
                    World world = BuildWorld();
                    var diag = new List<string>();
                    fingerprints[mode] = RunCanonicalAction(world, mode, sink, diag);
                }
                finally
                {
                    SimulationPresentationBridge.DetachSink(sink);
                }
            }
            SimulationPresentationBridge.ClearSinks();

            string baseline = fingerprints[ObservationMode.FullyRepresented];
            foreach (ObservationMode mode in modes)
            {
                Assert.AreEqual(baseline, fingerprints[mode],
                    $"observation mode {mode} changed the authoritative outcome");
            }

            // The sink heard one notification per action per mode: presentation
            // was told, but the telling changed nothing.
            Assert.IsTrue(baseline.Contains("sinkEvents=3"), "expected 3 notifications, got: " + baseline);
        }

        [Test]
        public void PresentationSinkFaults_NeverCorruptSimulation()
        {
            SimulationPresentationBridge.ClearSinks();
            var exploding = new ExplodingSink();
            var recording = new RecordingSink();
            SimulationPresentationBridge.AttachSink(exploding);
            SimulationPresentationBridge.AttachSink(recording);
            try
            {
                World world = BuildWorld();
                string fingerprint = RunCanonicalAction(world, ObservationMode.OffScreen, recording, new List<string>());
                Assert.IsTrue(fingerprint.Contains("ledger=5400"),
                    "simulation outcome intact despite sink fault: " + fingerprint);
                Assert.AreEqual(3, recording.Events.Count, "later sinks still notified");
            }
            finally
            {
                SimulationPresentationBridge.ClearSinks();
            }
        }

        private sealed class ExplodingSink : IObservationSink
        {
            public void Notify(ObservationMode mode, string eventName, string detail)
            {
                throw new InvalidOperationException("presentation fault");
            }
        }

        [Test]
        public void NoSinkAttached_SimulationUnchanged()
        {
            SimulationPresentationBridge.ClearSinks();
            Assert.AreEqual(0, SimulationPresentationBridge.SinkCount);

            World world = BuildWorld();
            var sink = new RecordingSink(); // never attached
            string fingerprint = RunCanonicalAction(world, ObservationMode.StreamedOut, sink, new List<string>());

            World world2 = BuildWorld();
            SimulationPresentationBridge.AttachSink(sink);
            try
            {
                string fingerprint2 = RunCanonicalAction(world2, ObservationMode.StreamedOut, sink, new List<string>());
                // Same outcomes; the only fingerprint difference is the sink event count.
                string norm1 = fingerprint.Replace("sinkEvents=0", "sinkEvents=X");
                string norm2 = fingerprint2.Replace("sinkEvents=3", "sinkEvents=X");
                Assert.AreEqual(norm1, norm2, "attaching a sink must not change outcomes");
            }
            finally
            {
                SimulationPresentationBridge.ClearSinks();
            }
        }
    }
}
